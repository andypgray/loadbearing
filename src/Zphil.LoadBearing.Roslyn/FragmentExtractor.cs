using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn.Caching;
using CoreAccessibility = Zphil.LoadBearing.Accessibility;
using CoreTypeKind = Zphil.LoadBearing.TypeKind;
using RoslynAccessibility = Microsoft.CodeAnalysis.Accessibility;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Extracts one <see cref="CompilationInput" /> into a self-contained <see cref="CodebaseFragment" />
///     by running the model's passes over a <em>single</em> compilation: declare the types, fill their
///     hierarchy, then mint each edge family. Each <c>Walk*</c> method below states what its own family
///     keys on.
/// </summary>
/// <remarks>
///     <para>
///         One rule holds across every edge family, so no pass restates it: sites dedupe by (file, line),
///         the self-edge is dropped, and each endpoint type decomposes to its definition-level FQN the way
///         a type edge does (GRAMMAR §4.1). What the passes actually divide on is <em>where they read</em>.
///         The source-name walk reads each declaring part's syntax and yields the type, member-use,
///         construction, catch and throw edges together (§4.5, §4.8), every channel riding independently of
///         the type channel. Injection (§4.7) and exposure (§4.9) are declaration-side passes over
///         constructors and public signature positions, needing no syntax walk at all. Registration facts
///         (§4.7) take a whole-compilation walk over every syntax tree, because a registration is a
///         string-side fact needing no per-type attribution — its most common composition root is a
///         top-level-statements <c>Program</c>, which declares no type to attribute it to.
///     </para>
///     <para>
///         Hierarchy fills <em>by FQN</em>, recording every referenced-but-not-declared FQN in
///         <see cref="CodebaseFragment.Externals" /> with the facts from this compilation's metadata view.
///         That is what makes a fragment self-contained, and it is why a type another project declares
///         still lands in <c>Externals</c> here: this is the per-input half of the split builder, and the
///         merge — not this pass — decides unification (declared-beats-external). See
///         <see cref="FragmentMerger" />.
///     </para>
/// </remarks>
internal static class FragmentExtractor
{
    private const string GeneratedCodeAttributeFullName = "System.CodeDom.Compiler.GeneratedCodeAttribute";

    private static readonly SymbolDisplayFormat FullNameFormat = new(
        SymbolDisplayGlobalNamespaceStyle.Omitted,
        SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        SymbolDisplayGenericsOptions.IncludeTypeParameters);

    public static CodebaseFragment Extract(CompilationInput input)
    {
        return new ExtractState().Run(input);
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

    // Scalar shape + identity facts read once from the original definition. Shape facts are normalized to
    // C# declaration semantics: a static class is encoded abstract+sealed in metadata (and may be so from
    // source), and the `&& !isStatic` mask converges both paths so a static class reports neither. All three
    // facts a symbol can fail to answer are total here, because the external-mint path reaches symbols the
    // compiler never resolved (a partially-loaded workspace yields error symbols through base types,
    // interfaces, and attribute classes): TypeKind falls back to Class, the baseline key (GRAMMAR §4.3) to
    // an unresolved:{fqn} form when the symbol has no DocumentationCommentId at all (an error symbol does
    // have one — Roslyn's "!:" form — so this covers unnamed types), and accessibility to Public — an
    // unresolved external is not a rule subject (§4.1), its accessibility is informational, and a type
    // reached across an assembly boundary is visibly-public surface. Declared types always map, so all
    // three fallbacks are no-ops for them.
    private static TypeFacts ExtractFacts(INamedTypeSymbol definition)
    {
        string fqn = FullNameOf(definition);
        CoreTypeKind kind = TypeKindMapper.TryMap(definition, out CoreTypeKind mapped) ? mapped : CoreTypeKind.Class;
        CoreAccessibility accessibility =
            AccessibilityMapper.TryMap(definition, out CoreAccessibility declared) ? declared : CoreAccessibility.Public;
        bool isStatic = definition.IsStatic;
        return new TypeFacts(
            fqn,
            definition.GetDocumentationCommentId() ?? "unresolved:" + fqn,
            definition.Name,
            NamespaceOf(definition),
            kind,
            accessibility,
            definition.IsSealed && !isStatic,
            isStatic,
            definition.IsAbstract && !isStatic,
            definition.IsRecord,
            IsGeneratedType(definition));
    }

    // The generated fact `.Authored()` filters on (GRAMMAR §5.2). A type is generated when
    // [System.CodeDom.Compiler.GeneratedCode] sits on it or on any type containing it, so the nested types a
    // generator emits inside an attributed container ride along without carrying their own attribute. The
    // attribute is the whole boundary — nothing is inferred from a file path, an obj/ directory, or a naming
    // convention. Reading the merged symbol is what decides the partial case: [GeneratedRegex] puts its
    // attribute on the generated METHOD, so the author's own partial class stays authored.
    private static bool IsGeneratedType(INamedTypeSymbol definition)
    {
        for (INamedTypeSymbol? current = definition; current is not null; current = current.ContainingType)
            if (current.GetAttributes().Any(IsGeneratedCodeAttribute))
                return true;

        return false;
    }

    private static bool IsGeneratedCodeAttribute(AttributeData attribute)
    {
        return attribute.AttributeClass is { } attributeClass && FullNameOf(attributeClass) == GeneratedCodeAttributeFullName;
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

    // The declared-member inventory of one type (GRAMMAR §4.6): its Ordinary methods, non-indexer
    // properties, fields, and events. Enum and delegate types contribute nothing — an enum's fields are its
    // values (an enum-value read stays a recorded member USE, §4.5) and a delegate's Invoke/BeginInvoke are
    // runtime plumbing. Ordered ordinal by the member's DocumentationCommentId, so the fragment (and the
    // merged node built from it) is deterministic.
    // Each survivor is kept paired with the symbol it was built from: the exposure pass (§4.9) screens this
    // same inventory rather than re-walking GetMembers and recomputing every member's declaration sites.
    private static IReadOnlyList<(ISymbol Symbol, FragmentMember Member)> BuildMembers(INamedTypeSymbol type, CoreTypeKind kind)
    {
        if (kind is CoreTypeKind.Enum or CoreTypeKind.Delegate) return [];

        var members = new List<(ISymbol Symbol, FragmentMember Member)>();
        foreach (ISymbol member in type.GetMembers())
            if (IsInventoried(member))
                members.Add((member, BuildMember(member)));

        members.Sort((left, right) => string.CompareOrdinal(left.Member.Facts.SymbolId, right.Member.Facts.SymbolId));
        return members;
    }

    // The ratified exclusion filter (GRAMMAR §4.6): drop every compiler-generated/implicitly-declared member
    // (auto-property and field-like-event backing fields, the record equality/clone/deconstruct surface),
    // every member with no source-writable name, every non-Ordinary method (which is exactly accessors,
    // constructors incl. static, operators, conversions, finalizers, and explicit interface METHOD
    // implementations), indexers, and anything that is not a method/property/field/event. Explicit interface
    // implementations are excluded uniformly across all three member kinds: a METHOD impl falls out via the
    // non-Ordinary MethodKind screen, a PROPERTY or EVENT impl via its non-empty
    // ExplicitInterfaceImplementations. An explicit impl is interface plumbing (Private, its name fixed by
    // the interface), never authored surface — so no member subject ever sees one.
    private static bool IsInventoried(ISymbol member)
    {
        if (member.IsImplicitlyDeclared) return false;

        // The member-side twin of the type-side screen in Declare: a member source cannot name is not
        // authored surface, so no rule can be about it. Subtracts exactly the synthesized top-level-statements
        // entry point `<Main>$` — every other unwritable member is already gone on the screens below — which a
        // project-noun subject reaches (the enclosing Program type IS authored surface and stays inventoried).
        if (!member.CanBeReferencedByName) return false;

        return member switch
        {
            IMethodSymbol method => method.MethodKind == MethodKind.Ordinary,
            IPropertySymbol { IsIndexer: false, ExplicitInterfaceImplementations.IsEmpty: true } => true,
            IFieldSymbol => true,
            IEventSymbol { ExplicitInterfaceImplementations.IsEmpty: true } => true,
            _ => false
        };
    }

    private static FragmentMember BuildMember(ISymbol member)
    {
        string symbolId = member.GetDocumentationCommentId()
                          ?? "unresolved:" + FullNameOf(member.ContainingType) + "." + member.Name;
        (string? returnTypeFullName, string? memberTypeFullName) = MemberTypeNames(member);

        var facts = new MemberFacts(
            symbolId,
            member.Name,
            MemberKindOf(member),
            AccessibilityMapper.Map(member),
            member.IsStatic,
            member.IsAbstract, // C# declaration semantics: an abstract member and every interface member report true
            member.IsVirtual, // false for an override or abstract member — an override is not itself "virtual"
            member is IMethodSymbol { IsAsync: true },
            returnTypeFullName,
            memberTypeFullName,
            ParametersOf(member),
            AttributeConstructionsOf(member));

        return new FragmentMember(facts, MemberDeclarationSites(member));
    }

    // The declared parameters of a member (GRAMMAR §4.6, §5.6): a method's own parameter list in declaration
    // order, each type normalized by the same DefinitionName helper the return type uses (a constructed generic
    // erases to its definition). The list is read off the DECLARED symbol, so an extension method's `this`
    // parameter is included and a default-valued parameter counts; ref/in/out leave the recorded type alone,
    // a `params T[]` records the array type, and a `T?` records System.Nullable<T>'s definition form. A
    // property/field/event carries no parameters, so it yields the empty list.
    private static IReadOnlyList<ParameterFacts> ParametersOf(ISymbol member)
    {
        if (member is not IMethodSymbol method) return [];

        return method.Parameters
            .Select(parameter => new ParameterFacts(parameter.Name, DefinitionName(parameter.Type)))
            .ToList();
    }

    // The declared attributes of a member (GRAMMAR §4.6): what the member symbol itself carries, each as the
    // same definition/constructed name pair the type-side attribute-construction fact records — so a C# 11
    // generic attribute's definition (N.MarkAttribute<T>) and its construction (N.MarkAttribute<System.Int32>)
    // are both readable, and for the ordinary non-generic case the two names coincide. DECLARED-ONLY is the
    // boundary: attributes are read off this one symbol, so a property's accessor attributes and a method's
    // [return:] attributes are deliberately outside the fact — they hang off different symbols (the accessor
    // method, the return-value pseudo-symbol) that no member subject ever ranges over. Unlike the type side
    // this mints nothing: the member model keeps its by-FQN-string discipline, so there is no ResolveName
    // here and no external node is created for an attribute only a member wears. Sorted ordinal by constructed
    // name so a persisted fragment is byte-stable however Roslyn happened to order the attribute list.
    private static IReadOnlyList<FragmentConstruction> AttributeConstructionsOf(ISymbol member)
    {
        return member.GetAttributes()
            .Select(a => a.AttributeClass)
            .Where(c => c is not null)
            .Select(c => new FragmentConstruction(DefinitionName(c!), c!.ToDisplayString(FullNameFormat)))
            .OrderBy(c => c.ConstructedName, StringComparer.Ordinal)
            .ToList();
    }

    // Exactly one of the two names is non-null: a method carries its return type (System.Void for void), a
    // property/field/event its member type. Both are the definition-level FQN in extraction format — the
    // return type read off OriginalDefinition so `Task<int>` matches a `.Returning(typeof(Task<>))` anchor
    // (§4.6), the same construction-erasing normalization ResolveName uses for edge targets (§4.1).
    private static (string? ReturnTypeFullName, string? MemberTypeFullName) MemberTypeNames(ISymbol member)
    {
        return member switch
        {
            IMethodSymbol method => (DefinitionName(method.ReturnType), null),
            IPropertySymbol property => (null, DefinitionName(property.Type)),
            IFieldSymbol field => (null, DefinitionName(field.Type)),
            IEventSymbol @event => (null, DefinitionName(@event.Type)),
            _ => (null, null)
        };
    }

    private static string DefinitionName(ITypeSymbol type)
    {
        return type.OriginalDefinition.ToDisplayString(FullNameFormat);
    }

    private static MemberKind MemberKindOf(ISymbol member)
    {
        return member switch
        {
            IMethodSymbol => MemberKind.Method,
            IPropertySymbol => MemberKind.Property,
            IFieldSymbol => MemberKind.Field,
            IEventSymbol => MemberKind.Event,
            _ => throw new ArgumentOutOfRangeException(nameof(member), member.Kind, "not an inventoried member symbol")
        };
    }

    // Declaration sites for a member: the identifier line (+1) of each declaring syntax — a partial method
    // has two, a field/field-like-event declarator has its own — deduped and ordered by (file, line), the
    // same discipline the type declaration-site pass uses.
    private static IReadOnlyList<FragmentSite> MemberDeclarationSites(ISymbol member)
    {
        var sites = new SortedSet<FragmentSite>();
        foreach (SyntaxReference reference in member.DeclaringSyntaxReferences)
        {
            SyntaxNode node = reference.GetSyntax();
            Location location = MemberIdentifierOf(node) is { } identifier ? identifier.GetLocation() : node.GetLocation();
            int line = location.GetLineSpan().StartLinePosition.Line + 1;
            sites.Add(new FragmentSite(node.SyntaxTree.FilePath, line));
        }

        return sites.ToList();
    }

    private static SyntaxToken? MemberIdentifierOf(SyntaxNode node)
    {
        return node switch
        {
            MethodDeclarationSyntax method => method.Identifier,
            PropertyDeclarationSyntax property => property.Identifier,
            EventDeclarationSyntax @event => @event.Identifier,
            VariableDeclaratorSyntax declarator => declarator.Identifier, // a field or a field-like event
            ParameterSyntax parameter => parameter.Identifier, // a positional record property
            _ => null
        };
    }

    // The definition-level endpoints one constructor-parameter type contributes as injection edges (GRAMMAR
    // §4.7), decomposed exactly like a type edge (§4.1): a constructed generic yields its definition and every
    // type argument (recursively), an array yields its element type (recursively), a plain named type yields
    // itself. Type parameters, pointers, and dynamic contribute nothing. Endpoints are gated the same way a
    // type-edge target is (implicitly declared / un-nameable / TypeKindMapper non-match are skipped).
    private static IEnumerable<INamedTypeSymbol> DecomposeType(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                foreach (INamedTypeSymbol endpoint in DecomposeType(array.ElementType))
                    yield return endpoint;
                break;

            case INamedTypeSymbol named:
                INamedTypeSymbol definition = named.OriginalDefinition;
                if (IsInjectableEndpoint(definition)) yield return definition;
                foreach (ITypeSymbol argument in named.TypeArguments)
                foreach (INamedTypeSymbol endpoint in DecomposeType(argument))
                    yield return endpoint;
                break;
        }
    }

    private static bool IsInjectableEndpoint(INamedTypeSymbol definition)
    {
        if (definition.IsImplicitlyDeclared || !definition.CanBeReferencedByName) return false;
        return TypeKindMapper.TryMap(definition, out _);
    }

    // The signature-position types one inventoried member contributes as exposure edges (GRAMMAR §4.9): a
    // method's return type (System.Void skipped — a void return names nothing) and each parameter type; a
    // property/field/event's own type. Only members IsInventoried admits reach here (Ordinary methods,
    // non-indexer properties, fields, events — never an explicit interface impl of any kind), so constructors,
    // accessors, operators, and the delegate Invoke never contribute. Each yielded type is decomposed
    // definition-level by DecomposeType, exactly like a
    // constructor-parameter type on the injection axis.
    private static IEnumerable<ITypeSymbol> SignatureTypesOf(ISymbol member)
    {
        switch (member)
        {
            case IMethodSymbol method:
                if (!method.ReturnsVoid) yield return method.ReturnType;
                foreach (IParameterSymbol parameter in method.Parameters)
                    yield return parameter.Type;
                break;
            case IPropertySymbol property:
                yield return property.Type;
                break;
            case IFieldSymbol field:
                yield return field.Type;
                break;
            case IEventSymbol @event:
                yield return @event.Type;
                break;
        }
    }

    // Effective visibility (GRAMMAR §4.9): the member is public AND every containing type up the chain is
    // public. A public member nested in an internal type is not surface, so it mints nothing — the honesty
    // boundary (an internal member is not part of the type's external contract). No explicit interface
    // implementation ever reaches this gate: IsInventoried (§4.6) drops all three kinds before the exposure
    // pass runs — a METHOD impl via the non-Ordinary MethodKind screen, a PROPERTY or EVENT impl via its
    // non-empty ExplicitInterfaceImplementations. So the inventory filter, not this gate, is what keeps
    // explicit impls (Private plumbing) out of the exposure graph.
    private static bool IsEffectivelyPublicMember(ISymbol member)
    {
        if (member.DeclaredAccessibility != RoslynAccessibility.Public) return false;
        for (INamedTypeSymbol? type = member.ContainingType; type is not null; type = type.ContainingType)
            if (type.DeclaredAccessibility != RoslynAccessibility.Public)
                return false;
        return true;
    }

    // The declaration site of a constructor parameter: its in-source identifier location (a primary-constructor
    // parameter, an ordinary-constructor parameter — both point at the parameter name), file path raw from the
    // syntax tree and 1-based line, the same discipline every other site pass uses. Null for a parameter with
    // no source location (should not arise for a source-declared constructor).
    private static FragmentSite? ParameterSite(IParameterSymbol parameter)
    {
        foreach (Location location in parameter.Locations)
            if (location.SourceTree is { } tree)
            {
                int line = location.GetLineSpan().StartLinePosition.Line + 1;
                return new FragmentSite(tree.FilePath, line);
            }

        return null;
    }

    // The site of a registration call: the invoked method-name identifier (so a fluent chain records each
    // .AddX() on its own line, not the shared receiver line), file path raw from the syntax tree, 1-based line.
    private static FragmentSite RegistrationSite(InvocationExpressionSyntax invocation)
    {
        SyntaxNode anchor = invocation.Expression is MemberAccessExpressionSyntax memberAccess
            ? memberAccess.Name
            : invocation.Expression;
        int line = anchor.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        return new FragmentSite(anchor.SyntaxTree.FilePath, line);
    }

    private sealed class ExtractState
    {
        private readonly Dictionary<(string Src, string Caught), SortedSet<FragmentSite>> _catchEdgeSites = new();

        // The swallowing subset of _catchEdgeUnfilteredSites, keyed identically (GRAMMAR §4.8): unfiltered AND
        // not ending in a throw. Recorded as its own parallel fact for the same polarity reason as the
        // unfiltered subset — a ban must read the sites it forbids directly, never a complement, because sites
        // dedupe by (file, line) and a complement would hide the swallowing clause of a same-line collision.
        private readonly Dictionary<(string Src, string Caught), SortedSet<FragmentSite>> _catchEdgeSwallowingSites = new();

        // The unfiltered subset of _catchEdgeSites, keyed identically (GRAMMAR §4.8). Recorded as its own
        // parallel fact rather than derived by complement: sites dedupe by (file, line), so a filtered and an
        // unfiltered catch of one type on one physical line collapse to a single site, and a complement would
        // then hide the unfiltered clause. Recording the unfiltered sites is truthful for a ban either way.
        private readonly Dictionary<(string Src, string Caught), SortedSet<FragmentSite>> _catchEdgeUnfilteredSites = new();
        private readonly Dictionary<(string Src, string Ctor), SortedSet<FragmentSite>> _constructorEdgeSites = new();
        private readonly Dictionary<string, DeclaredBuilder> _declared = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Src, string Tgt), SortedSet<FragmentSite>> _edgeSites = new();
        private readonly Dictionary<(string Src, string Exposed), SortedSet<FragmentSite>> _exposureEdgeSites = new();
        private readonly Dictionary<string, FragmentExternal> _externals = new(StringComparer.Ordinal);

        // The per-compilation memo behind this state's FullNameOf. The display walk is the most expensive
        // per-reference operation in extraction and the same handful of symbols recur across thousands of
        // sites, so the memo is keyed on OriginalDefinition — what FullNameOf reads — and every construction of
        // one generic shares its entry. It holds symbols, so it is scoped to the state that dies with the
        // compilation.
        private readonly Dictionary<ISymbol, string> _fullNames = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<(string Src, string Injected), SortedSet<FragmentSite>> _injectionEdgeSites = new();
        private readonly Dictionary<(string Src, string MemberSymbolId), MemberEdgeBuilder> _memberEdges = new();
        private readonly Dictionary<(Lifetime Lifetime, string Service, string? Impl), SortedSet<FragmentSite>> _registrationSites = new();
        private readonly Dictionary<(string Src, string Thrown), SortedSet<FragmentSite>> _throwEdgeSites = new();

        public CodebaseFragment Run(CompilationInput input)
        {
            Compilation compilation = input.Compilation;

            // Pass 1 — declare every solution-declared type in this compilation.
            foreach (INamedTypeSymbol symbol in DeclaredTypes(compilation))
                Declare(symbol);

            // Pass 2 — hierarchy (once per declared type, in first-declaration order).
            foreach (DeclaredBuilder builder in _declared.Values)
                PopulateHierarchy(builder);

            // Pass 3 — edges (every declaring part; sites dedupe).
            foreach (DeclaredBuilder builder in _declared.Values)
                WalkEdges(builder.Symbol, compilation);

            // Pass 3b — injection edges (declared instance constructors; primary ctors included).
            foreach (DeclaredBuilder builder in _declared.Values)
                WalkInjectionEdges(builder.Symbol);

            // Pass 3c — exposure edges (signature positions of effectively-public members). Its own
            // declaration-side pass, never folded into the Pass-1 inventory loop: an in-solution endpoint must
            // resolve against the fully-populated _declared table, not be minted as an external mid-inventory.
            foreach (DeclaredBuilder builder in _declared.Values)
                WalkExposureEdges(builder);

            // Pass R — registration facts (whole-compilation walk; a top-level-statements Program is not a
            // declared type, so a per-declared-type walk would miss the most common composition root).
            WalkRegistrations(compilation);

            return Materialize(input);
        }

        private void Declare(INamedTypeSymbol symbol)
        {
            if (symbol.IsImplicitlyDeclared || !symbol.CanBeReferencedByName) return;

            if (!TypeKindMapper.TryMap(symbol, out _)) return;

            INamedTypeSymbol definition = symbol.OriginalDefinition;
            string fqn = FullNameOf(definition);

            if (!_declared.TryGetValue(fqn, out DeclaredBuilder? builder))
            {
                TypeFacts facts = ExtractFacts(definition);
                builder = new DeclaredBuilder(facts, definition, BuildMembers(definition, facts.Kind));
                _declared[fqn] = builder;
            }

            AccumulateDeclarationSites(builder.Sites, definition);
        }

        private void PopulateHierarchy(DeclaredBuilder builder)
        {
            INamedTypeSymbol symbol = builder.Symbol;

            builder.BaseTypeFullName = symbol.BaseType is { } baseType ? ResolveName(baseType) : null;

            builder.Interfaces = symbol.Interfaces
                .Select(ResolveName)
                .ToList();

            builder.Attributes = symbol.GetAttributes()
                .Select(a => a.AttributeClass)
                .Where(c => c is not null)
                .Select(c => ResolveName(c!))
                .ToList();

            // Construction-preserving facts: the Definition side resolves to the OriginalDefinition FQN
            // (via ResolveName), while the constructed name displays the CONSTRUCTED symbol, so a closed
            // generic keeps its substituted arguments.
            builder.AllInterfaces = symbol.AllInterfaces
                .Select(Construction)
                .OrderBy(c => c.ConstructedName, StringComparer.Ordinal)
                .ToList();

            var baseChain = new List<FragmentConstruction>();
            for (INamedTypeSymbol? current = symbol.BaseType; current is not null; current = current.BaseType)
                baseChain.Add(Construction(current)); // nearest-first; derivation order is meaningful, so not sorted
            builder.BaseTypeChain = baseChain;

            builder.AttributeConstructions = symbol.GetAttributes()
                .Select(a => a.AttributeClass)
                .Where(c => c is not null)
                .Select(c => Construction(c!))
                .OrderBy(c => c.ConstructedName, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>A construction: the definition FQN (via <see cref="ResolveName" />) plus the constructed display name.</summary>
        private FragmentConstruction Construction(INamedTypeSymbol symbol)
        {
            return new FragmentConstruction(ResolveName(symbol), symbol.ToDisplayString(FullNameFormat));
        }

        private void WalkEdges(INamedTypeSymbol symbol, Compilation compilation)
        {
            string srcFqn = FullNameOf(symbol);
            foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
            {
                SyntaxNode root = reference.GetSyntax();
                SemanticModel model = compilation.GetSemanticModel(root.SyntaxTree);
                foreach ((INamedTypeSymbol? target, ISymbol? member, INamedTypeSymbol? constructed, INamedTypeSymbol? caught, bool caughtHasFilter, bool caughtEndsInThrow, INamedTypeSymbol? thrown, string file, int line)
                         in ReferenceWalker.Walk(root, model))
                {
                    var site = new FragmentSite(file, line);

                    // The type channel: mint the reference edge (and any member use riding it), self-edges dropped.
                    if (target is not null)
                    {
                        string tgtFqn = FullNameOf(target);
                        if (tgtFqn != srcFqn) // self-edge — drops the type edge and any member use on this node
                        {
                            ResolveName(target);
                            FragmentSiteSets.For(_edgeSites, (srcFqn, tgtFqn)).Add(site);

                            // The member's containing type is the type-channel target (they agree by construction),
                            // so it is already resolved above; the member edge reuses tgtFqn and records the member.
                            if (member is not null)
                                RecordMemberEdge(srcFqn, tgtFqn, member, site);
                        }
                    }

                    // The construct channel: mint the construction edge, self-construction dropped to mirror the
                    // type-edge self-drop (§4.1). Rides independently of the type channel — an explicit `new Foo()`
                    // arrives here with target=null, so a shared self-edge `continue` would silently drop it.
                    if (constructed is not null)
                    {
                        string ctorFqn = FullNameOf(constructed);
                        if (ctorFqn != srcFqn)
                        {
                            ResolveName(constructed);
                            FragmentSiteSets.For(_constructorEdgeSites, (srcFqn, ctorFqn)).Add(site);
                        }
                    }

                    // The catch channel (§4.8): mint the catch edge, self-catch dropped like the type-edge self-drop.
                    // Rides independently — a typed catch arrives here with target=null (its type-name syntax minted
                    // the reference edge on its own visit) and a bare catch names no type at all. The same site also
                    // joins the edge's unfiltered subset when the clause spells no `when` filter, so the subset holds
                    // by construction; a bare `catch` is unfiltered, a bare `catch when (…)` is not. It joins the
                    // swallowing subset in turn when it is unfiltered AND its block does not end in a throw, so the
                    // subset-of-a-subset holds by construction too.
                    if (caught is not null)
                    {
                        string caughtFqn = FullNameOf(caught);
                        if (caughtFqn != srcFqn)
                        {
                            ResolveName(caught);
                            FragmentSiteSets.For(_catchEdgeSites, (srcFqn, caughtFqn)).Add(site);
                            if (!caughtHasFilter) FragmentSiteSets.For(_catchEdgeUnfilteredSites, (srcFqn, caughtFqn)).Add(site);
                            if (!caughtHasFilter && !caughtEndsInThrow)
                                FragmentSiteSets.For(_catchEdgeSwallowingSites, (srcFqn, caughtFqn)).Add(site);
                        }
                    }

                    // The throw channel (§4.8): mint the throw edge, self-throw dropped like the type-edge self-drop.
                    // Rides independently of the construct channel — a `throw new X()` mints BOTH the construction
                    // edge (above) and the throw edge here at the same site.
                    if (thrown is not null)
                    {
                        string thrownFqn = FullNameOf(thrown);
                        if (thrownFqn != srcFqn)
                        {
                            ResolveName(thrown);
                            FragmentSiteSets.For(_throwEdgeSites, (srcFqn, thrownFqn)).Add(site);
                        }
                    }
                }
            }
        }

        // One member edge per (source, member DocumentationCommentId); sites union like the type-edge pass. The
        // SymbolId fallback mirrors TypeFacts's (unresolved:{fqn}) for a member with no DocID (error/unnamed).
        private void RecordMemberEdge(string srcFqn, string containingFqn, ISymbol member, FragmentSite site)
        {
            string memberSymbolId = member.GetDocumentationCommentId() ?? "unresolved:" + containingFqn + "." + member.Name;

            if (!_memberEdges.TryGetValue((srcFqn, memberSymbolId), out MemberEdgeBuilder? builder))
            {
                builder = new MemberEdgeBuilder(containingFqn, member.Name, MemberKindOf(member));
                _memberEdges[(srcFqn, memberSymbolId)] = builder;
            }

            builder.Sites.Add(site);
        }

        // Injection edges (GRAMMAR §4.7): the declared instance constructors of one type. Primary constructors
        // are included; the !IsImplicitlyDeclared filter drops the record copy constructor and the parameterless
        // default, and InstanceConstructors already excludes the static constructor. Each parameter's type
        // decomposes definition-level like a type edge (§4.1); self-injection is dropped like the type-edge
        // self-drop; ResolveName runs on every decomposed endpoint so external injected types get nodes.
        private void WalkInjectionEdges(INamedTypeSymbol symbol)
        {
            string srcFqn = FullNameOf(symbol);
            foreach (IMethodSymbol ctor in symbol.InstanceConstructors)
            {
                if (ctor.IsImplicitlyDeclared) continue;

                foreach (IParameterSymbol parameter in ctor.Parameters)
                {
                    if (ParameterSite(parameter) is not { } site) continue;

                    foreach (INamedTypeSymbol endpoint in DecomposeType(parameter.Type))
                    {
                        string injectedFqn = ResolveName(endpoint);
                        if (injectedFqn == srcFqn) continue; // self-injection dropped
                        FragmentSiteSets.For(_injectionEdgeSites, (srcFqn, injectedFqn)).Add(site);
                    }
                }
            }
        }

        // Exposure edges (GRAMMAR §4.9): a declaration-side pass over the members of one type. An edge is minted
        // from every public signature position (SignatureTypesOf: a method's return + parameter types, a
        // property/field/event's type) of each effectively-public member — an inventoried member (§4.6) that is
        // itself public AND nested only in public types. Each signature type decomposes definition-level like a
        // type edge (§4.1); self-exposure is dropped like the type-edge self-drop (which also self-drops an enum
        // value's self-typing); ResolveName runs on every decomposed endpoint so external exposed types get
        // nodes. The member's declaration sites (a partial member's several parts union) are the edge's sites.
        // The pass screens pass 1's inventory, so the §4.6 filter and the sites are read, not recomputed; the
        // two kinds that inventory nothing contribute nothing here either — a delegate's members are every one
        // implicitly declared (so §4.6 already drops them all), and an enum's values are typed by the enum
        // itself and so only ever self-dropped.
        private void WalkExposureEdges(DeclaredBuilder builder)
        {
            string srcFqn = FullNameOf(builder.Symbol);
            foreach ((ISymbol symbol, FragmentMember member) in builder.Members)
            {
                if (!IsEffectivelyPublicMember(symbol)) continue;

                foreach (ITypeSymbol signatureType in SignatureTypesOf(symbol))
                foreach (INamedTypeSymbol endpoint in DecomposeType(signatureType))
                {
                    string exposedFqn = ResolveName(endpoint);
                    if (exposedFqn == srcFqn) continue; // self-exposure dropped (enum value self-typing self-drops here)
                    var sites = FragmentSiteSets.For(_exposureEdgeSites, (srcFqn, exposedFqn));
                    foreach (FragmentSite site in member.DeclarationSites) sites.Add(site);
                }
            }
        }

        // Registration facts (GRAMMAR §4.7): a whole-compilation walk over every syntax tree. Registration is a
        // string-side fact needing no per-type attribution — its most common composition root is a
        // top-level-statements Program — so one pass over every tree is the natural formulation. Each
        // recognized call (RegistrationRecognizer) yields a (lifetime, service, implementation?) fact recorded
        // string-side (definition-level FQNs, never resolved to nodes — registration is many-to-many).
        private void WalkRegistrations(Compilation compilation)
        {
            var recognizer = new RegistrationRecognizer(compilation);
            if (!recognizer.IsActive) return; // MEDI not referenced — nothing can be recognized

            foreach (SyntaxTree tree in compilation.SyntaxTrees)
            {
                SemanticModel model = compilation.GetSemanticModel(tree);
                foreach (InvocationExpressionSyntax invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
                foreach (RecognizedRegistration registration in recognizer.Recognize(invocation, model))
                    RecordRegistration(registration, RegistrationSite(invocation));
            }
        }

        private void RecordRegistration(RecognizedRegistration registration, FragmentSite site)
        {
            string serviceFqn = FullNameOf(registration.Service);
            string? implFqn = registration.Implementation is { } impl ? FullNameOf(impl) : null;
            FragmentSiteSets.For(_registrationSites, (registration.Lifetime, serviceFqn, implFqn)).Add(site);
        }

        /// <summary>
        ///     The memoized <see cref="FragmentExtractor.FullNameOf" />. Declaring it here hides the outer
        ///     static one throughout <see cref="ExtractState" />, so every name this state reads — the walk's
        ///     four channels, each pass's source name, <see cref="ResolveName" />'s own lookup — comes back
        ///     through the memo, and the same symbol is displayed once per compilation rather than once per
        ///     site.
        /// </summary>
        private string FullNameOf(INamedTypeSymbol symbol)
        {
            INamedTypeSymbol definition = symbol.OriginalDefinition;
            if (_fullNames.TryGetValue(definition, out string? fqn)) return fqn;

            fqn = FragmentExtractor.FullNameOf(definition);
            _fullNames[definition] = fqn;
            return fqn;
        }

        /// <summary>
        ///     The FQN of a referenced type's original definition. When that FQN is not declared by this
        ///     input, it is a referenced-but-not-declared type, so an external record (first mention wins)
        ///     is minted from this compilation's metadata view — the per-input analog of the builder's node
        ///     minting, deferring cross-input unification to the merge.
        /// </summary>
        private string ResolveName(INamedTypeSymbol symbol)
        {
            INamedTypeSymbol definition = symbol.OriginalDefinition;
            string fqn = FullNameOf(definition);

            if (!_declared.ContainsKey(fqn) && !_externals.ContainsKey(fqn))
                _externals[fqn] = new FragmentExternal(ExtractFacts(definition), definition.ContainingAssembly?.Name ?? "");

            return fqn;
        }

        private static void AccumulateDeclarationSites(SortedSet<FragmentSite> sites, INamedTypeSymbol definition)
        {
            foreach (SyntaxReference reference in definition.DeclaringSyntaxReferences)
            {
                SyntaxNode node = reference.GetSyntax();
                if (IdentifierOf(node) is { } identifier)
                {
                    int line = identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    sites.Add(new FragmentSite(node.SyntaxTree.FilePath, line));
                }
            }
        }

        private CodebaseFragment Materialize(CompilationInput input)
        {
            // Canonical ordering makes the serialized fragment stable; the merge re-derives global order,
            // so a fragment's internal order never affects the model.
            var declaredTypes = _declared.Values
                .Select(b => b.ToFragmentType())
                .OrderBy(t => t.Facts.FullName, StringComparer.Ordinal)
                .ToList();

            var externals = _externals.Values
                .OrderBy(e => e.Facts.FullName, StringComparer.Ordinal)
                .ToList();

            var edges = FragmentSiteSets.OrderedPairs(
                _edgeSites, (src, tgt, sites) => new FragmentEdge(src, tgt, sites.ToList()));

            var memberEdges = FragmentSiteSets.OrderedPairs(
                _memberEdges,
                (src, symbolId, builder) => new FragmentMemberEdge(
                    src, builder.ContainingFullName, builder.MemberName, symbolId, builder.Kind, builder.Sites.ToList()));

            var constructorEdges = FragmentSiteSets.OrderedPairs(
                _constructorEdgeSites, (src, ctor, sites) => new FragmentConstructorEdge(src, ctor, sites.ToList()));

            var injectionEdges = FragmentSiteSets.OrderedPairs(
                _injectionEdgeSites, (src, injected, sites) => new FragmentInjectionEdge(src, injected, sites.ToList()));

            // An edge whose every site is filtered has no entry in either parallel table, which materializes as
            // the empty list — the honest reading, since nothing about that edge is unfiltered. An edge whose
            // every unfiltered site ends in a throw materializes the third list empty for the same reason.
            var catchEdges = FragmentSiteSets.OrderedPairs(
                _catchEdgeSites,
                (src, caught, sites) => new FragmentCatchEdge(
                    src, caught, sites.ToList(),
                    _catchEdgeUnfilteredSites.TryGetValue((src, caught), out var unfiltered) ? unfiltered.ToList() : [],
                    _catchEdgeSwallowingSites.TryGetValue((src, caught), out var swallowing) ? swallowing.ToList() : []));

            var throwEdges = FragmentSiteSets.OrderedPairs(
                _throwEdgeSites, (src, thrown, sites) => new FragmentThrowEdge(src, thrown, sites.ToList()));

            var exposureEdges = FragmentSiteSets.OrderedPairs(
                _exposureEdgeSites, (src, exposed, sites) => new FragmentExposureEdge(src, exposed, sites.ToList()));

            var serviceRegistrations = _registrationSites
                .OrderBy(kv => kv.Key.Lifetime)
                .ThenBy(kv => kv.Key.Service, StringComparer.Ordinal)
                .ThenBy(kv => kv.Key.Impl ?? "", StringComparer.Ordinal)
                .Select(kv => new FragmentServiceRegistration(kv.Key.Lifetime, kv.Key.Service, kv.Key.Impl, kv.Value.ToList()))
                .ToList();

            return new CodebaseFragment(
                input.ProjectName, input.ProjectReferences, declaredTypes, externals, edges, memberEdges, constructorEdges,
                injectionEdges, catchEdges, throwEdges, exposureEdges, serviceRegistrations);
        }
    }

    /// <summary>
    ///     Mutable accumulator for one declared type: fixed facts + representative symbol, unioned declaration sites, and
    ///     hierarchy filled in pass 2.
    /// </summary>
    private sealed class DeclaredBuilder(
        TypeFacts facts,
        INamedTypeSymbol symbol,
        IReadOnlyList<(ISymbol Symbol, FragmentMember Member)> members)
    {
        public TypeFacts Facts { get; } = facts;
        public INamedTypeSymbol Symbol { get; } = symbol;

        /// <summary>
        ///     The §4.6 inventory, each member paired with the symbol it was read from — pass 1's work, held so
        ///     the exposure pass reads the members and their declaration sites rather than deriving them twice.
        /// </summary>
        public IReadOnlyList<(ISymbol Symbol, FragmentMember Member)> Members { get; } = members;

        public SortedSet<FragmentSite> Sites { get; } = [];
        public string? BaseTypeFullName { get; set; }
        public IReadOnlyList<string> Interfaces { get; set; } = [];
        public IReadOnlyList<string> Attributes { get; set; } = [];
        public IReadOnlyList<FragmentConstruction> AllInterfaces { get; set; } = [];
        public IReadOnlyList<FragmentConstruction> BaseTypeChain { get; set; } = [];
        public IReadOnlyList<FragmentConstruction> AttributeConstructions { get; set; } = [];

        public FragmentType ToFragmentType()
        {
            return new FragmentType(
                Facts,
                Sites.ToList(),
                BaseTypeFullName,
                Interfaces,
                Attributes,
                AllInterfaces,
                BaseTypeChain,
                AttributeConstructions,
                Members.Select(m => m.Member).ToList());
        }
    }

    /// <summary>
    ///     Mutable accumulator for one member-use edge, keyed by (source FQN, member DocumentationCommentId): the
    ///     member's declaring-type FQN, simple name, and kind (all functions of the member symbol) plus its unioned
    ///     use sites. The facts are captured once on first mention; every later site of the same member unions in.
    /// </summary>
    private sealed class MemberEdgeBuilder(string containingFullName, string memberName, MemberKind kind)
    {
        public string ContainingFullName { get; } = containingFullName;
        public string MemberName { get; } = memberName;
        public MemberKind Kind { get; } = kind;
        public SortedSet<FragmentSite> Sites { get; } = [];
    }
}
