using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn.Caching;
using CoreTypeKind = Zphil.LoadBearing.TypeKind;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Extracts one <see cref="CompilationInput" /> into a self-contained <see cref="CodebaseFragment" />
///     by running the model's passes over a <em>single</em> compilation:
///     <list type="number">
///         <item>Declare — record a <see cref="FragmentType" /> per solution-declared type in this input.</item>
///         <item>
///             Hierarchy — fill each declared type's base type, interfaces, and attributes <em>by FQN</em>,
///             recording every referenced-but-not-declared FQN in <see cref="CodebaseFragment.Externals" />
///             with the facts from this compilation's metadata view.
///         </item>
///         <item>
///             Edges — walk each declaring part's source for source-name-derived reference edges, deduped by (file,
///             line), self-edges dropped. The same walk yields the member-use edges (GRAMMAR §4.5) beside the type
///             edges — one per (source, member DocumentationCommentId), sites deduped, same-type uses dropped — the
///             construction edges (§4.5) — one per (source, constructed FQN) at every object-creation
///             expression, sites deduped, self-construction dropped — and the catch and throw edges (§4.8) — one
///             per (source, caught FQN) at every <c>catch</c> clause and one per (source, thrown FQN) at every
///             <c>throw</c> statement/expression, sites deduped, self-catch/self-throw dropped. Every channel
///             rides independently of the type channel.
///         </item>
///         <item>
///             Injection edges (GRAMMAR §4.7) — a declaration-side pass over each declared type's
///             <see cref="INamedTypeSymbol.InstanceConstructors" /> (primary constructors included; implicitly
///             declared ones — the record copy constructor, the parameterless default — filtered out), minting
///             one <see cref="FragmentInjectionEdge" /> per (source, injected FQN) at every constructor
///             parameter, with the parameter type decomposed definition-level like a type edge, self-injection
///             dropped, sites deduped.
///         </item>
///         <item>
///             Registration facts (GRAMMAR §4.7) — a whole-compilation walk over every syntax tree, minting
///             one <see cref="FragmentServiceRegistration" /> per (lifetime, service FQN, implementation FQN)
///             recognized call, sites deduped. Registration is a string-side fact needing no per-type
///             attribution (its most common composition root is a top-level-statements <c>Program</c>), so one
///             pass over every tree is the natural formulation. See <see cref="RegistrationRecognizer" />.
///         </item>
///     </list>
///     This is the per-input half of the split builder: <see cref="FragmentMerger" /> unifies a set of
///     fragments by FQN to reproduce the global cross-input semantics. Because a fragment sees only its own
///     compilation, a type another project declares still lands in <see cref="CodebaseFragment.Externals" />
///     here; the merge — not this pass — decides unification (declared-beats-external).
/// </summary>
internal static class FragmentExtractor
{
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
    // source), and the `&& !isStatic` mask converges both paths so a static class reports neither. The
    // TryMap fallback to Class matches the external-mint path (declared types always map, so it is a no-op
    // for them). The baseline key (GRAMMAR §4.3) is the definition's DocumentationCommentId (T: form), or
    // an unresolved:{fqn} fallback when the symbol has no DocID (error/unnamed types).
    private static TypeFacts ExtractFacts(INamedTypeSymbol definition)
    {
        string fqn = FullNameOf(definition);
        CoreTypeKind kind = TypeKindMapper.TryMap(definition, out CoreTypeKind mapped) ? mapped : CoreTypeKind.Class;
        bool isStatic = definition.IsStatic;
        return new TypeFacts(
            fqn,
            definition.GetDocumentationCommentId() ?? "unresolved:" + fqn,
            definition.Name,
            NamespaceOf(definition),
            kind,
            AccessibilityMapper.Map(definition),
            definition.IsSealed && !isStatic,
            isStatic,
            definition.IsAbstract && !isStatic,
            definition.IsRecord);
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
    private static IReadOnlyList<FragmentMember> BuildMembers(INamedTypeSymbol type, CoreTypeKind kind)
    {
        if (kind is CoreTypeKind.Enum or CoreTypeKind.Delegate) return [];

        var members = new List<FragmentMember>();
        foreach (ISymbol member in type.GetMembers())
            if (IsInventoried(member))
                members.Add(BuildMember(member));

        members.Sort((left, right) => string.CompareOrdinal(left.Facts.SymbolId, right.Facts.SymbolId));
        return members;
    }

    // The ratified exclusion filter (GRAMMAR §4.6): drop every compiler-generated/implicitly-declared member
    // (auto-property and field-like-event backing fields, the record equality/clone/deconstruct surface),
    // every non-Ordinary method (which is exactly accessors, constructors incl. static, operators,
    // conversions, and finalizers), indexers, and anything that is not a method/property/field/event.
    private static bool IsInventoried(ISymbol member)
    {
        if (member.IsImplicitlyDeclared) return false;

        return member switch
        {
            IMethodSymbol method => method.MethodKind == MethodKind.Ordinary,
            IPropertySymbol property => !property.IsIndexer,
            IFieldSymbol => true,
            IEventSymbol => true,
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
            memberTypeFullName);

        return new FragmentMember(facts, MemberDeclarationSites(member));
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
        private readonly Dictionary<(string Src, string Ctor), SortedSet<FragmentSite>> _constructorEdgeSites = new();
        private readonly Dictionary<string, DeclaredBuilder> _declared = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Src, string Tgt), SortedSet<FragmentSite>> _edgeSites = new();
        private readonly Dictionary<string, FragmentExternal> _externals = new(StringComparer.Ordinal);
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
                foreach ((INamedTypeSymbol? target, ISymbol? member, INamedTypeSymbol? constructed, INamedTypeSymbol? caught, INamedTypeSymbol? thrown, string file, int line)
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
                            EdgeSites((srcFqn, tgtFqn)).Add(site);

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
                            ConstructorEdgeSites((srcFqn, ctorFqn)).Add(site);
                        }
                    }

                    // The catch channel (§4.8): mint the catch edge, self-catch dropped like the type-edge self-drop.
                    // Rides independently — a typed catch arrives here with target=null (its type-name syntax minted
                    // the reference edge on its own visit) and a bare catch names no type at all.
                    if (caught is not null)
                    {
                        string caughtFqn = FullNameOf(caught);
                        if (caughtFqn != srcFqn)
                        {
                            ResolveName(caught);
                            CatchEdgeSites((srcFqn, caughtFqn)).Add(site);
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
                            ThrowEdgeSites((srcFqn, thrownFqn)).Add(site);
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
                        InjectionEdgeSites((srcFqn, injectedFqn)).Add(site);
                    }
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
            RegistrationSites((registration.Lifetime, serviceFqn, implFqn)).Add(site);
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

            var edges = _edgeSites
                .OrderBy(kv => kv.Key.Src, StringComparer.Ordinal)
                .ThenBy(kv => kv.Key.Tgt, StringComparer.Ordinal)
                .Select(kv => new FragmentEdge(kv.Key.Src, kv.Key.Tgt, kv.Value.ToList()))
                .ToList();

            var memberEdges = _memberEdges
                .OrderBy(kv => kv.Key.Src, StringComparer.Ordinal)
                .ThenBy(kv => kv.Key.MemberSymbolId, StringComparer.Ordinal)
                .Select(kv => new FragmentMemberEdge(
                    kv.Key.Src, kv.Value.ContainingFullName, kv.Value.MemberName, kv.Key.MemberSymbolId, kv.Value.Kind,
                    kv.Value.Sites.ToList()))
                .ToList();

            var constructorEdges = _constructorEdgeSites
                .OrderBy(kv => kv.Key.Src, StringComparer.Ordinal)
                .ThenBy(kv => kv.Key.Ctor, StringComparer.Ordinal)
                .Select(kv => new FragmentConstructorEdge(kv.Key.Src, kv.Key.Ctor, kv.Value.ToList()))
                .ToList();

            var injectionEdges = _injectionEdgeSites
                .OrderBy(kv => kv.Key.Src, StringComparer.Ordinal)
                .ThenBy(kv => kv.Key.Injected, StringComparer.Ordinal)
                .Select(kv => new FragmentInjectionEdge(kv.Key.Src, kv.Key.Injected, kv.Value.ToList()))
                .ToList();

            var catchEdges = _catchEdgeSites
                .OrderBy(kv => kv.Key.Src, StringComparer.Ordinal)
                .ThenBy(kv => kv.Key.Caught, StringComparer.Ordinal)
                .Select(kv => new FragmentCatchEdge(kv.Key.Src, kv.Key.Caught, kv.Value.ToList()))
                .ToList();

            var throwEdges = _throwEdgeSites
                .OrderBy(kv => kv.Key.Src, StringComparer.Ordinal)
                .ThenBy(kv => kv.Key.Thrown, StringComparer.Ordinal)
                .Select(kv => new FragmentThrowEdge(kv.Key.Src, kv.Key.Thrown, kv.Value.ToList()))
                .ToList();

            var serviceRegistrations = _registrationSites
                .OrderBy(kv => kv.Key.Lifetime)
                .ThenBy(kv => kv.Key.Service, StringComparer.Ordinal)
                .ThenBy(kv => kv.Key.Impl ?? "", StringComparer.Ordinal)
                .Select(kv => new FragmentServiceRegistration(kv.Key.Lifetime, kv.Key.Service, kv.Key.Impl, kv.Value.ToList()))
                .ToList();

            return new CodebaseFragment(
                input.ProjectName, input.ProjectReferences, declaredTypes, externals, edges, memberEdges, constructorEdges,
                injectionEdges, catchEdges, throwEdges, serviceRegistrations);
        }

        private SortedSet<FragmentSite> EdgeSites((string Src, string Tgt) key)
        {
            if (!_edgeSites.TryGetValue(key, out var sites))
            {
                sites = new SortedSet<FragmentSite>();
                _edgeSites[key] = sites;
            }

            return sites;
        }

        private SortedSet<FragmentSite> ConstructorEdgeSites((string Src, string Ctor) key)
        {
            if (!_constructorEdgeSites.TryGetValue(key, out var sites))
            {
                sites = new SortedSet<FragmentSite>();
                _constructorEdgeSites[key] = sites;
            }

            return sites;
        }

        private SortedSet<FragmentSite> CatchEdgeSites((string Src, string Caught) key)
        {
            if (!_catchEdgeSites.TryGetValue(key, out var sites))
            {
                sites = new SortedSet<FragmentSite>();
                _catchEdgeSites[key] = sites;
            }

            return sites;
        }

        private SortedSet<FragmentSite> ThrowEdgeSites((string Src, string Thrown) key)
        {
            if (!_throwEdgeSites.TryGetValue(key, out var sites))
            {
                sites = new SortedSet<FragmentSite>();
                _throwEdgeSites[key] = sites;
            }

            return sites;
        }

        private SortedSet<FragmentSite> InjectionEdgeSites((string Src, string Injected) key)
        {
            if (!_injectionEdgeSites.TryGetValue(key, out var sites))
            {
                sites = new SortedSet<FragmentSite>();
                _injectionEdgeSites[key] = sites;
            }

            return sites;
        }

        private SortedSet<FragmentSite> RegistrationSites((Lifetime Lifetime, string Service, string? Impl) key)
        {
            if (!_registrationSites.TryGetValue(key, out var sites))
            {
                sites = new SortedSet<FragmentSite>();
                _registrationSites[key] = sites;
            }

            return sites;
        }
    }

    /// <summary>
    ///     Mutable accumulator for one declared type: fixed facts + representative symbol, unioned declaration sites, and
    ///     hierarchy filled in pass 2.
    /// </summary>
    private sealed class DeclaredBuilder(TypeFacts facts, INamedTypeSymbol symbol, IReadOnlyList<FragmentMember> members)
    {
        public TypeFacts Facts { get; } = facts;
        public INamedTypeSymbol Symbol { get; } = symbol;
        public IReadOnlyList<FragmentMember> Members { get; } = members;
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
                Members);
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