using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Walks a single type-declaration part and yields, per node, five channels at once:
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
///             <c>using static</c> bare name — GRAMMAR §4.5's "a source-level member access";
///         </item>
///         <item>
///             the <b>constructed</b> type an object-creation expression creates — explicit <c>new Foo()</c>
///             and target-typed <c>new()</c> — GRAMMAR §4.5's "a source-level object creation". Delegate
///             creation (<c>new Action(M)</c> and its target-typed form) is excluded, keyed on the created
///             symbol's <see cref="Microsoft.CodeAnalysis.TypeKind.Delegate" /> so both spellings skip;
///         </item>
///         <item>
///             the <b>caught</b> type a <c>catch</c> clause catches — the declared type of a typed
///             <c>catch (IOException e)</c>, or synthesized <c>System.Exception</c> for a bare <c>catch</c>
///             — GRAMMAR §4.8's "a source-level <c>catch</c> clause"; and
///         </item>
///         <item>
///             the <b>thrown</b> type a <c>throw</c> statement or throw expression throws — the thrown
///             expression's <em>static</em> (natural, never converted) type — GRAMMAR §4.8's "a source-level
///             <c>throw</c>". A bare rethrow (<c>throw;</c>) throws nothing.
///         </item>
///     </list>
///     The member channel is <see langword="null" /> whenever the name is not a §4.5 use: type-only
///     references, constructors, operators/conversions, local functions and accessors reached as methods,
///     and — the pinned asymmetry — <c>nameof</c> operands (which still mint their type edge; a
///     <c>nameof</c> never <em>uses</em> the member it names). The construct channel is non-<see langword="null" />
///     only on the two object-creation arms; every other node leaves it null (attribute applications,
///     <c>base(…)</c>/<c>this(…)</c> initializers, <c>with</c> expressions, and array creation carry no
///     object-creation expression, so their exclusion falls out of the syntax). The caught channel is
///     non-<see langword="null" /> only on the <c>catch</c>-clause arm and the thrown channel only on the
///     two throw arms; a typed catch rides the caught channel with a null type channel (its type-name syntax
///     mints the §4.1 reference on its own visit — no double-mint, the explicit-<c>new</c> precedent), and a
///     bare catch names no type at all so mints no reference edge. The type channel is exactly the tuple
///     this walker yielded before member edges existed, unchanged for every input — an explicit
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
    public static IEnumerable<(INamedTypeSymbol? Target, ISymbol? Member, INamedTypeSymbol? Constructed, INamedTypeSymbol? Caught, INamedTypeSymbol? Thrown, string File, int Line)> Walk(
        SyntaxNode root, SemanticModel model)
    {
        foreach (SyntaxNode node in root.DescendantNodes(n => ReferenceEquals(n, root)
                                                              || n is not (BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)))
        {
            (INamedTypeSymbol? target, ISymbol? member, INamedTypeSymbol? constructed, INamedTypeSymbol? caught, INamedTypeSymbol? thrown) = Resolve(node, model);

            // Each channel is normalized to its OriginalDefinition and gated independently, so an explicit
            // `new Foo()` (which rides on Target: null, Constructed: Foo) — and a bare `catch` (Target: null,
            // Caught: Exception) — is never dropped by a type-channel guard. The co-existence matrix rows are
            // what catch a regression here.
            INamedTypeSymbol? targetDefinition = ReferencedDefinition(target);
            INamedTypeSymbol? constructedDefinition = ReferencedDefinition(constructed);
            INamedTypeSymbol? caughtDefinition = ReferencedDefinition(caught);
            INamedTypeSymbol? thrownDefinition = ReferencedDefinition(thrown);
            if (targetDefinition is null && constructedDefinition is null && caughtDefinition is null && thrownDefinition is null) continue;

            // A member use rides the type channel; if that channel is gated out, its member goes with it.
            ISymbol? memberUse = targetDefinition is not null ? member : null;

            int line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            yield return (targetDefinition, memberUse, constructedDefinition, caughtDefinition, thrownDefinition, node.SyntaxTree.FilePath, line);
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
    ///     constructs, the §4.8 type it catches, the §4.8 type it throws). The type is <see langword="null" />
    ///     when the node is not a reference at all (namespaces, type parameters, locals/params, discards,
    ///     dynamic, arrays/pointers, predefined-type keywords, and unresolved names all fall through). The
    ///     member is <see langword="null" /> whenever the node is not a member use (a constructor is never a
    ///     use). The constructed channel is non-null only on the two object-creation arms, and null there too
    ///     for a delegate creation. The caught channel is non-null only on the <c>catch</c>-clause arm; the
    ///     thrown channel only on the two throw arms (a bare rethrow leaves it null).
    /// </summary>
    private static (INamedTypeSymbol? Target, ISymbol? Member, INamedTypeSymbol? Constructed, INamedTypeSymbol? Caught, INamedTypeSymbol? Thrown) Resolve(
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
                return (ContainingTypeOf(symbol), MemberUseOf(symbol, name), null, null, null);

            // Explicit `new Foo()`: the inner `Foo` name syntax already mints the type edge on its own visit,
            // so this node contributes the construct channel ONLY — re-minting the type edge here would
            // double-count the §4.1 reference.
            case ObjectCreationExpressionSyntax creation:
                return (null, null, ConstructedChannelOf(CreatedTypeOf(creation, model)), null, null);

            // Target-typed `new()`: no inner type-name syntax exists, so this node is the sole source of BOTH
            // the type edge to the created type AND the construct edge (the type edge rides even for a
            // delegate `new()`, only the construct channel is delegate-gated).
            case ImplicitObjectCreationExpressionSyntax creation:
                INamedTypeSymbol? created = CreatedTypeOf(creation, model);
                return (created, null, ConstructedChannelOf(created), null, null);

            // A `catch` clause (§4.8): the ONE arm for catches — it reads `.Declaration` for the typed form
            // and synthesizes System.Exception for a bare `catch`, so there is no separate CatchDeclaration
            // arm (a typed catch's type-name syntax is a SimpleName visited on its own, minting the reference
            // edge; this arm carries the caught channel ONLY — no double-mint). A `when` filter is an ordinary
            // sub-expression walked on its own visits, so it never suppresses this edge.
            case CatchClauseSyntax catchClause:
                return (null, null, null, CaughtTypeOf(catchClause, model), null);

            // A `throw` statement with a non-null expression (§4.8): a bare rethrow `throw;` has a null
            // expression, so it never matches this arm and falls through — it throws nothing.
            case ThrowStatementSyntax { Expression: { } thrownExpression }:
                return (null, null, null, null, ThrownTypeOf(thrownExpression, model));

            // A throw expression (§4.8): `?? throw …`, a conditional/switch-expression arm, and the
            // expression-bodied `=> throw new X()` — its operand is always present.
            case ThrowExpressionSyntax throwExpression:
                return (null, null, null, null, ThrownTypeOf(throwExpression.Expression, model));

            default:
                return (null, null, null, null, null);
        }
    }

    /// <summary>
    ///     The named type a <c>catch</c> clause catches (GRAMMAR §4.8): the declared type of a typed
    ///     <c>catch (T e)</c> / <c>catch (T)</c> read off <see cref="CatchClauseSyntax.Declaration" />, or
    ///     synthesized <c>System.Exception</c> for a bare <c>catch</c> — nothing in source names the type, so
    ///     the synthesized type is the only source of the caught channel there. A null metadata lookup (a
    ///     compilation lacking <c>System.Exception</c>) yields null, minting nothing.
    /// </summary>
    private static INamedTypeSymbol? CaughtTypeOf(CatchClauseSyntax catchClause, SemanticModel model)
    {
        if (catchClause.Declaration is { } declaration)
            return model.GetTypeInfo(declaration.Type).Type as INamedTypeSymbol;

        return model.Compilation.GetTypeByMetadataName("System.Exception");
    }

    /// <summary>
    ///     The named type a <c>throw</c> expression throws (GRAMMAR §4.8): the thrown expression's NATURAL
    ///     static type — <see cref="TypeInfo.Type" />, never <see cref="TypeInfo.ConvertedType" />, because
    ///     the throw conversion would collapse every thrown expression to <c>System.Exception</c> and erase
    ///     the real type. A <c>throw ex</c> yields the variable's static type; <c>throw null</c> and a
    ///     type-parameter throw yield null (no named type), minting nothing.
    /// </summary>
    private static INamedTypeSymbol? ThrownTypeOf(ExpressionSyntax expression, SemanticModel model)
    {
        return model.GetTypeInfo(expression).Type as INamedTypeSymbol;
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
    ///     symbol reference, not a runtime use. The walk up the parent chain is bounded by the enclosing
    ///     statement/member (a <c>nameof</c>, if present, is reached first), so it stays local and cheap.
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