using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Walks a single type-declaration part and yields, per node, three channels at once:
///     <list type="bullet">
///         <item>
///             the <b>type</b> it binds a name to (base lists, signatures, generic args, <c>typeof</c>,
///             casts, creations, static receivers, attribute names, <c>nameof</c> operands, and the
///             containing type of any member access) — GRAMMAR §4.1's "a source-level type reference";
///         </item>
///         <item>
///             the <b>member</b> it uses, when the name resolved through one — a method invocation or
///             method-group reference, a property/field/event access (including <c>?.</c>, compound
///             assignment, and <c>+=</c>/<c>-=</c> subscription), a reduced extension call, or a
///             <c>using static</c> bare name — GRAMMAR §4.5's "a source-level member access"; and
///         </item>
///         <item>
///             the <b>constructed</b> type an object-creation expression creates — explicit <c>new Foo()</c>
///             and target-typed <c>new()</c> — GRAMMAR §4.5's "a source-level object creation". Delegate
///             creation (<c>new Action(M)</c> and its target-typed form) is excluded, keyed on the created
///             symbol's <see cref="Microsoft.CodeAnalysis.TypeKind.Delegate" /> so both spellings skip.
///         </item>
///     </list>
///     The member channel is <see langword="null" /> whenever the name is not a §4.5 use: type-only
///     references, constructors, operators/conversions, local functions and accessors reached as methods,
///     and — the pinned asymmetry — <c>nameof</c> operands (which still mint their type edge; a
///     <c>nameof</c> never <em>uses</em> the member it names). The construct channel is non-<see langword="null" />
///     only on the two object-creation arms; every other node leaves it null (attribute applications,
///     <c>base(…)</c>/<c>this(…)</c> initializers, <c>with</c> expressions, and array creation carry no
///     object-creation expression, so their exclusion falls out of the syntax). The type channel is exactly
///     the tuple this walker yielded before member edges existed, unchanged for every input — an explicit
///     <c>new Foo()</c> still mints its type edge from the inner <c>Foo</c> name, never re-minted here.
/// </summary>
/// <remarks>
///     Nested type declarations are a hard boundary: <c>descendIntoChildren</c> refuses to descend
///     into a <see cref="BaseTypeDeclarationSyntax" />/<see cref="DelegateDeclarationSyntax" /> other
///     than the root, so a nested type's references belong to the nested node (walked independently)
///     and file-level <c>using</c> directives — which sit outside any type — are never attributed to
///     a type. Type targets are normalized to their <c>OriginalDefinition</c> and gated by
///     <see cref="TypeKindMapper" />; member symbols are normalized to definition level too (a reduced
///     extension through <c>ReducedFrom</c>, then every member through <c>OriginalDefinition</c>), so a
///     yielded member's <c>ContainingType</c> agrees with the type channel's target for that node. A
///     repeated bind on one line yields duplicate tuples that the caller dedupes by (file, line).
/// </remarks>
internal static class ReferenceWalker
{
    public static IEnumerable<(INamedTypeSymbol? Target, ISymbol? Member, INamedTypeSymbol? Constructed, string File, int Line)> Walk(
        SyntaxNode root, SemanticModel model)
    {
        foreach (SyntaxNode node in root.DescendantNodes(n => ReferenceEquals(n, root)
                                                              || n is not (BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)))
        {
            (INamedTypeSymbol? target, ISymbol? member, INamedTypeSymbol? constructed) = Resolve(node, model);

            // Each channel is normalized to its OriginalDefinition and gated independently, so an explicit
            // `new Foo()` (which rides on Target: null, Constructed: Foo) is never dropped by a type-channel
            // guard — the co-existence matrix rows are what catch a regression here.
            INamedTypeSymbol? targetDefinition = ReferencedDefinition(target);
            INamedTypeSymbol? constructedDefinition = ReferencedDefinition(constructed);
            if (targetDefinition is null && constructedDefinition is null) continue;

            // A member use rides the type channel; if that channel is gated out, its member goes with it.
            ISymbol? memberUse = targetDefinition is not null ? member : null;

            int line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            yield return (targetDefinition, memberUse, constructedDefinition, node.SyntaxTree.FilePath, line);
        }
    }

    /// <summary>
    ///     A referenced type's original definition, or <see langword="null" /> when the symbol is absent or
    ///     gated out — implicitly declared, un-nameable, or a <see cref="TypeKindMapper" /> non-match. Applied
    ///     independently to the type and construct channels so each stands or falls on its own.
    /// </summary>
    private static INamedTypeSymbol? ReferencedDefinition(INamedTypeSymbol? symbol)
    {
        if (symbol is null) return null;

        var definition = (INamedTypeSymbol)symbol.OriginalDefinition;
        if (definition.IsImplicitlyDeclared || !definition.CanBeReferencedByName) return null;
        if (!TypeKindMapper.TryMap(definition, out _)) return null;

        return definition;
    }

    /// <summary>
    ///     Resolves a syntax node to (the type it binds a name to, the §4.5 member it uses, the type it
    ///     constructs). The type is <see langword="null" /> when the node is not a reference at all
    ///     (namespaces, type parameters, locals/params, discards, dynamic, arrays/pointers, predefined-type
    ///     keywords, and unresolved names all fall through). The member is <see langword="null" /> whenever
    ///     the node is not a member use (a constructor is never a use). The constructed channel is non-null
    ///     only on the two object-creation arms, and null there too for a delegate creation.
    /// </summary>
    private static (INamedTypeSymbol? Target, ISymbol? Member, INamedTypeSymbol? Constructed) Resolve(
        SyntaxNode node, SemanticModel model)
    {
        switch (node)
        {
            // IdentifierNameSyntax (excluding the `var` contextual keyword) and GenericNameSyntax.
            // Covers type references and member references (invocations, property/field/event access,
            // static receivers, attribute names, method groups, extension methods, nameof operands).
            case SimpleNameSyntax name when name is not IdentifierNameSyntax { IsVar: true }:
                SymbolInfo info = model.GetSymbolInfo(name);
                ISymbol? symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
                return (ContainingTypeOf(symbol), MemberUseOf(symbol, name), null);

            // Explicit `new Foo()`: the inner `Foo` name syntax already mints the type edge on its own visit,
            // so this node contributes the construct channel ONLY — re-minting the type edge here would
            // double-count the §4.1 reference.
            case ObjectCreationExpressionSyntax creation:
                return (null, null, ConstructedChannelOf(CreatedTypeOf(creation, model)));

            // Target-typed `new()`: no inner type-name syntax exists, so this node is the sole source of BOTH
            // the type edge to the created type AND the construct edge (the type edge rides even for a
            // delegate `new()`, only the construct channel is delegate-gated).
            case ImplicitObjectCreationExpressionSyntax creation:
                INamedTypeSymbol? created = CreatedTypeOf(creation, model);
                return (created, null, ConstructedChannelOf(created));

            default:
                return (null, null, null);
        }
    }

    /// <summary>
    ///     The named type an object-creation expression creates: the resolved constructor's containing type,
    ///     or (fallback) the expression's target type. Shared by the explicit and target-typed arms.
    /// </summary>
    private static INamedTypeSymbol? CreatedTypeOf(BaseObjectCreationExpressionSyntax creation, SemanticModel model)
    {
        SymbolInfo ctorInfo = model.GetSymbolInfo(creation);
        ISymbol? ctor = ctorInfo.Symbol ?? ctorInfo.CandidateSymbols.FirstOrDefault();
        return (ctor as IMethodSymbol)?.ContainingType ?? model.GetTypeInfo(creation).Type as INamedTypeSymbol;
    }

    /// <summary>
    ///     The construct channel for a created type: the type itself, unless it is a delegate. Delegate
    ///     creation (<c>new Action(M)</c> and its target-typed form) is not a construction edge (GRAMMAR
    ///     §4.5), keyed on the created symbol's <see cref="Microsoft.CodeAnalysis.TypeKind.Delegate" /> so
    ///     both spellings skip regardless of syntax.
    /// </summary>
    private static INamedTypeSymbol? ConstructedChannelOf(INamedTypeSymbol? created)
    {
        return created is { TypeKind: Microsoft.CodeAnalysis.TypeKind.Delegate } ? null : created;
    }

    private static INamedTypeSymbol? ContainingTypeOf(ISymbol? symbol)
    {
        return symbol switch
        {
            INamedTypeSymbol type => type,
            IMethodSymbol method => method.ContainingType,
            IPropertySymbol property => property.ContainingType,
            IFieldSymbol field => field.ContainingType,
            IEventSymbol @event => @event.ContainingType,
            _ => null
        };
    }

    /// <summary>
    ///     The normalized member a name uses (GRAMMAR §4.5), or <see langword="null" /> when the name is
    ///     not a member use. A <c>nameof</c> operand is excluded here (member channel only) so its type
    ///     edge still rides through unchanged. Methods are filtered to <see cref="MethodKind.Ordinary" />
    ///     and <see cref="MethodKind.ReducedExtension" /> — constructors, operators, conversions, local
    ///     functions, and accessors are not uses — then normalized <c>ReducedFrom</c>-first (a reduced
    ///     extension call is a use of the declaring static class's method) and <c>OriginalDefinition</c>-last;
    ///     properties/fields/events go straight to <c>OriginalDefinition</c> (an accessor already resolves
    ///     to its property/event).
    /// </summary>
    private static ISymbol? MemberUseOf(ISymbol? symbol, SyntaxNode node)
    {
        if (symbol is null || IsNameofOperand(node)) return null;

        switch (symbol)
        {
            case IMethodSymbol method when method.MethodKind is MethodKind.Ordinary or MethodKind.ReducedExtension:
                IMethodSymbol normalized = method.ReducedFrom ?? method;
                return normalized.OriginalDefinition;

            case IPropertySymbol property:
                return property.OriginalDefinition;

            case IFieldSymbol field:
                return field.OriginalDefinition;

            case IEventSymbol @event:
                return @event.OriginalDefinition;

            default:
                return null;
        }
    }

    /// <summary>
    ///     True when <paramref name="node" /> sits inside a <c>nameof(...)</c> operand — a compile-time
    ///     symbol reference, not a runtime use. Adapted from Zphil.Roz's <c>ReferenceKindClassifier</c>:
    ///     the walk up the parent chain is bounded by the enclosing statement/member (a <c>nameof</c>, if
    ///     present, is reached first), so it stays local and cheap.
    /// </summary>
    private static bool IsNameofOperand(SyntaxNode node)
    {
        for (SyntaxNode? current = node; current is not null; current = current.Parent)
        {
            if (current is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "nameof" } })
                return true;

            if (current is StatementSyntax or MemberDeclarationSyntax) break;
        }

        return false;
    }
}