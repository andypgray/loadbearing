using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zphil.LoadBearing.Codebase;
using CoreAccessibility = Zphil.LoadBearing.Accessibility;
using CoreTypeKind = Zphil.LoadBearing.TypeKind;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Builds a <see cref="CodebaseModel" /> from a set of compilations in three passes:
///     <list type="number">
///         <item>
///             Declare — mint a node per solution-declared type (all inputs before pass 2, so
///             cross-project references unify to the declaring node by FQN).
///         </item>
///         <item>
///             Hierarchy — fill each declared node's symbol-derived base type, direct interfaces,
///             and attributes (minting shallow external nodes as needed).
///         </item>
///         <item>
///             Edges — walk each declaring part's source and accumulate source-name-derived
///             reference edges, deduped by (file, line) and self-edges dropped.
///         </item>
///     </list>
///     Everything is materialized with the pinned ordinal ordering (types, edges, sites).
/// </summary>
internal static class CodebaseModelBuilder
{
    private static readonly SymbolDisplayFormat FullNameFormat = new(
        SymbolDisplayGlobalNamespaceStyle.Omitted,
        SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        SymbolDisplayGenericsOptions.IncludeTypeParameters);

    // Ordinal by file path, then line — the pinned site ordering (note: 'V' < 'c', so a
    // `*.Validation.cs` part sorts before its `*.cs` sibling).
    private static readonly IComparer<(string File, int Line)> SiteOrder =
        Comparer<(string File, int Line)>.Create((a, b) =>
        {
            int byFile = string.CompareOrdinal(a.File, b.File);
            return byFile != 0 ? byFile : a.Line.CompareTo(b.Line);
        });

    public static CodebaseModel Build(IReadOnlyList<CompilationInput> inputs)
    {
        return new BuildState().Run(inputs);
    }

    private static IEnumerable<INamedTypeSymbol> DeclaredTypes(Compilation compilation)
    {
        return TypesInNamespace(compilation.Assembly.GlobalNamespace);
    }

    private static IEnumerable<INamedTypeSymbol> TypesInNamespace(INamespaceSymbol ns)
    {
        foreach (INamespaceSymbol child in ns.GetNamespaceMembers())
        foreach (INamedTypeSymbol type in TypesInNamespace(child))
            yield return type;

        foreach (INamedTypeSymbol type in ns.GetTypeMembers())
        foreach (INamedTypeSymbol nested in TypeAndNested(type))
            yield return nested;
    }

    private static IEnumerable<INamedTypeSymbol> TypeAndNested(INamedTypeSymbol type)
    {
        yield return type;
        foreach (INamedTypeSymbol nested in type.GetTypeMembers())
        foreach (INamedTypeSymbol descendant in TypeAndNested(nested))
            yield return descendant;
    }

    private static string FullNameOf(INamedTypeSymbol symbol)
    {
        return symbol.OriginalDefinition.ToDisplayString(FullNameFormat);
    }

    // Shape facts normalized to C# declaration semantics. A static class is encoded abstract+sealed
    // in metadata (and may be so from source); the `&& !isStatic` mask converges both paths so a
    // static class reports neither sealed nor abstract. Kind-implied values (interfaces abstract;
    // structs/enums/delegates sealed) flow straight through from the symbol.
    private static (CoreAccessibility Accessibility, bool IsSealed, bool IsStatic, bool IsAbstract, bool IsRecord)
        ShapeFacts(INamedTypeSymbol definition)
    {
        bool isStatic = definition.IsStatic;
        return (AccessibilityMapper.Map(definition),
            definition.IsSealed && !isStatic, isStatic, definition.IsAbstract && !isStatic,
            definition.IsRecord);
    }

    // The baseline key (GRAMMAR §4.3): the definition's DocumentationCommentId (T: form), or an
    // unresolved:{fqn} fallback when the symbol has no DocID (error/unnamed types).
    private static string SymbolIdOf(INamedTypeSymbol definition, string fqn)
    {
        return definition.GetDocumentationCommentId() ?? "unresolved:" + fqn;
    }

    private static string NamespaceOf(INamedTypeSymbol symbol)
    {
        return symbol.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : "";
    }

    private static SyntaxToken? IdentifierOf(SyntaxNode node)
    {
        return node switch
        {
            BaseTypeDeclarationSyntax type => type.Identifier,
            DelegateDeclarationSyntax del => del.Identifier,
            _ => null
        };
    }

    private sealed class BuildState
    {
        private readonly Dictionary<string, SortedSet<(string File, int Line)>> _declarationSites = new(StringComparer.Ordinal);
        private readonly List<(INamedTypeSymbol Symbol, Compilation Compilation)> _declared = [];
        private readonly Dictionary<(string Src, string Tgt), SortedSet<(string File, int Line)>> _edgeSites = new();
        private readonly Dictionary<string, TypeNode> _nodesByFqn = new(StringComparer.Ordinal);
        private readonly Dictionary<string, INamedTypeSymbol> _representativeSymbol = new(StringComparer.Ordinal);

        public CodebaseModel Run(IReadOnlyList<CompilationInput> inputs)
        {
            // Pass 1 — declare (all inputs before any later pass, so cross-project targets unify).
            foreach (CompilationInput input in inputs)
            foreach (INamedTypeSymbol symbol in DeclaredTypes(input.Compilation))
                Declare(symbol, input.ProjectName, input.Compilation);

            // Pass 2 — hierarchy (once per declared node).
            foreach ((string fqn, INamedTypeSymbol symbol) in _representativeSymbol) PopulateHierarchy(_nodesByFqn[fqn], symbol);

            // Pass 3 — edges (every declaring part of every occurrence; sites dedupe).
            foreach ((INamedTypeSymbol symbol, Compilation compilation) in _declared) WalkEdges(symbol, compilation);

            return Materialize(inputs);
        }

        private void Declare(INamedTypeSymbol symbol, string projectName, Compilation compilation)
        {
            if (symbol.IsImplicitlyDeclared || !symbol.CanBeReferencedByName) return;

            if (!TypeKindMapper.TryMap(symbol, out CoreTypeKind kind)) return;

            INamedTypeSymbol definition = symbol.OriginalDefinition;
            string fqn = FullNameOf(definition);

            if (!_nodesByFqn.ContainsKey(fqn))
            {
                (CoreAccessibility Accessibility, bool IsSealed, bool IsStatic, bool IsAbstract, bool IsRecord) shape = ShapeFacts(definition);
                _nodesByFqn[fqn] = new TypeNode(
                    fqn, SymbolIdOf(definition, fqn), definition.Name, NamespaceOf(definition), kind,
                    shape.Accessibility, shape.IsSealed, shape.IsStatic, shape.IsAbstract, shape.IsRecord,
                    projectName, false);
                _representativeSymbol[fqn] = definition;
            }

            AccumulateDeclarationSites(fqn, definition);
            _declared.Add((definition, compilation));
        }

        private void PopulateHierarchy(TypeNode node, INamedTypeSymbol symbol)
        {
            if (symbol.BaseType is { } baseType) node.BaseType = GetOrAddNode(baseType);

            node.Interfaces = symbol.Interfaces
                .Select(i => (ITypeInfo)GetOrAddNode(i))
                .ToList();

            node.Attributes = symbol.GetAttributes()
                .Select(a => a.AttributeClass)
                .Where(c => c is not null)
                .Select(c => (ITypeInfo)GetOrAddNode(c!))
                .ToList();

            // Construction-preserving hierarchy facts (Phase 3). The Definition side reuses
            // GetOrAddNode (which normalizes to OriginalDefinition); the FullName side displays the
            // CONSTRUCTED symbol, so a closed generic keeps its substituted arguments.
            node.AllInterfaces = symbol.AllInterfaces
                .Select(Construction)
                .OrderBy(c => c.FullName, StringComparer.Ordinal)
                .ToList();

            var baseChain = new List<TypeConstruction>();
            for (INamedTypeSymbol? current = symbol.BaseType; current is not null; current = current.BaseType)
                baseChain.Add(Construction(current)); // nearest-first; derivation order is meaningful, so not sorted
            node.BaseTypeChain = baseChain;

            node.AttributeConstructions = symbol.GetAttributes()
                .Select(a => a.AttributeClass)
                .Where(c => c is not null)
                .Select(c => Construction(c!))
                .OrderBy(c => c.FullName, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>A construction: the definition node (via <see cref="GetOrAddNode" />) plus the constructed FullName.</summary>
        private TypeConstruction Construction(INamedTypeSymbol symbol)
        {
            return new TypeConstruction(GetOrAddNode(symbol), symbol.ToDisplayString(FullNameFormat));
        }

        private void WalkEdges(INamedTypeSymbol symbol, Compilation compilation)
        {
            string srcFqn = FullNameOf(symbol);
            foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
            {
                SyntaxNode root = reference.GetSyntax();
                SemanticModel model = compilation.GetSemanticModel(root.SyntaxTree);
                foreach ((INamedTypeSymbol target, string file, int line) in ReferenceWalker.Walk(root, model))
                {
                    string tgtFqn = FullNameOf(target);
                    if (tgtFqn == srcFqn) continue; // self-edge

                    GetOrAddNode(target);
                    Sites(_edgeSites, (srcFqn, tgtFqn)).Add((file, line));
                }
            }
        }

        /// <summary>Existing node for the type's FQN, or a freshly minted shallow external node.</summary>
        private TypeNode GetOrAddNode(INamedTypeSymbol symbol)
        {
            INamedTypeSymbol definition = symbol.OriginalDefinition;
            string fqn = FullNameOf(definition);
            if (_nodesByFqn.TryGetValue(fqn, out TypeNode? existing)) return existing;

            CoreTypeKind kind = TypeKindMapper.TryMap(definition, out CoreTypeKind mapped) ? mapped : CoreTypeKind.Class;
            (CoreAccessibility Accessibility, bool IsSealed, bool IsStatic, bool IsAbstract, bool IsRecord) shape = ShapeFacts(definition);
            var node = new TypeNode(
                fqn,
                SymbolIdOf(definition, fqn),
                definition.Name,
                NamespaceOf(definition),
                kind,
                shape.Accessibility, shape.IsSealed, shape.IsStatic, shape.IsAbstract, shape.IsRecord,
                definition.ContainingAssembly?.Name ?? "",
                true);
            _nodesByFqn[fqn] = node;
            return node;
        }

        private void AccumulateDeclarationSites(string fqn, INamedTypeSymbol definition)
        {
            var sites = Sites(_declarationSites, fqn);
            foreach (SyntaxReference reference in definition.DeclaringSyntaxReferences)
            {
                SyntaxNode node = reference.GetSyntax();
                if (IdentifierOf(node) is { } identifier)
                {
                    int line = identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    sites.Add((node.SyntaxTree.FilePath, line));
                }
            }
        }

        private CodebaseModel Materialize(IReadOnlyList<CompilationInput> inputs)
        {
            foreach ((string fqn, var sites) in _declarationSites)
            {
                TypeNode node = _nodesByFqn[fqn];
                node.DeclarationSites = ToLocations(sites);
                // The sites set is already (file, line) ordinal-ordered, so Distinct preserves
                // first-occurrence file order (the GRAMMAR §5.6 FilePaths contract).
                node.FilePaths = sites.Select(s => s.File).Distinct(StringComparer.Ordinal).ToList();
            }

            var types = _nodesByFqn.Values
                .OrderBy(n => n.FullName, StringComparer.Ordinal)
                .ToList();

            var edges = _edgeSites
                .OrderBy(kv => kv.Key.Src, StringComparer.Ordinal)
                .ThenBy(kv => kv.Key.Tgt, StringComparer.Ordinal)
                .Select(kv => new ReferenceEdge(_nodesByFqn[kv.Key.Src], _nodesByFqn[kv.Key.Tgt], ToLocations(kv.Value)))
                .ToList();

            return new CodebaseModel(types, edges, BuildProjects(inputs));
        }

        private static List<ProjectNode> BuildProjects(IReadOnlyList<CompilationInput> inputs)
        {
            Dictionary<string, SortedSet<string>> refsByProject = new(StringComparer.Ordinal);
            foreach (CompilationInput input in inputs)
            {
                if (!refsByProject.TryGetValue(input.ProjectName, out var refs))
                {
                    refs = new SortedSet<string>(StringComparer.Ordinal);
                    refsByProject[input.ProjectName] = refs;
                }

                foreach (string reference in input.ProjectReferences) refs.Add(reference);
            }

            return refsByProject
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new ProjectNode(kv.Key, kv.Value.ToList()))
                .ToList();
        }

        private static IReadOnlyList<SourceLocation> ToLocations(SortedSet<(string File, int Line)> sites)
        {
            return sites.Select(s => new SourceLocation(s.File, s.Line)).ToList();
        }

        private static SortedSet<(string File, int Line)> Sites<TKey>(
            Dictionary<TKey, SortedSet<(string File, int Line)>> map, TKey key)
            where TKey : notnull
        {
            if (!map.TryGetValue(key, out var sites))
            {
                sites = new SortedSet<(string File, int Line)>(SiteOrder);
                map[key] = sites;
            }

            return sites;
        }
    }
}