using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Walks a single type-declaration part and yields, per bound name, two channels at once:
///     <list type="bullet">
///         <item>
///             the <b>type</b> it binds a name to (base lists, signatures, generic args, <c>typeof</c>,
///             casts, creations, static receivers, attribute names, <c>nameof</c> operands, and the
///             containing type of any member access) — GRAMMAR §4.1's "a source-level type reference"; and
///         </item>
///         <item>
///             the <b>member</b> it uses, when the name resolved through one — a method invocation or
///             method-group reference, a property/field/event access (including <c>?.</c>, compound
///             assignment, and <c>+=</c>/<c>-=</c> subscription), a reduced extension call, or a
///             <c>using static</c> bare name — GRAMMAR §4.5's "a source-level member access".
///         </item>
///     </list>
///     The member channel is <see langword="null" /> whenever the name is not a §4.5 use: type-only
///     references, constructors, operators/conversions, local functions and accessors reached as methods,
///     and — the pinned asymmetry — <c>nameof</c> operands (which still mint their type edge; a
///     <c>nameof</c> never <em>uses</em> the member it names). The type channel is exactly the tuple this
///     walker yielded before member edges existed, unchanged for every input.
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
    public static IEnumerable<(INamedTypeSymbol Target, ISymbol? Member, string File, int Line)> Walk(
        SyntaxNode root, SemanticModel model)
    {
        foreach (SyntaxNode node in root.DescendantNodes(n => ReferenceEquals(n, root)
                                                              || n is not (BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)))
        {
            (INamedTypeSymbol? target, ISymbol? member) = Resolve(node, model);
            if (target is null) continue;

            var definition = (INamedTypeSymbol)target.OriginalDefinition;
            if (definition.IsImplicitlyDeclared || !definition.CanBeReferencedByName) continue;

            if (!TypeKindMapper.TryMap(definition, out _)) continue;

            int line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            yield return (definition, member, node.SyntaxTree.FilePath, line);
        }
    }

    /// <summary>
    ///     Resolves a syntax node to (the type it binds a name to, the §4.5 member it uses). The type
    ///     is <see langword="null" /> when the node is not a reference at all (namespaces, type
    ///     parameters, locals/params, discards, dynamic, arrays/pointers, predefined-type keywords, and
    ///     unresolved names all fall through). The member is <see langword="null" /> whenever the node
    ///     is type-only — including the target-typed <c>new()</c> arm, which is a constructor (never a use).
    /// </summary>
    private static (INamedTypeSymbol? Target, ISymbol? Member) Resolve(SyntaxNode node, SemanticModel model)
    {
        switch (node)
        {
            // IdentifierNameSyntax (excluding the `var` contextual keyword) and GenericNameSyntax.
            // Covers type references and member references (invocations, property/field/event access,
            // static receivers, attribute names, method groups, extension methods, nameof operands).
            case SimpleNameSyntax name when name is not IdentifierNameSyntax { IsVar: true }:
                SymbolInfo info = model.GetSymbolInfo(name);
                ISymbol? symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
                return (ContainingTypeOf(symbol), MemberUseOf(symbol, name));

            // Target-typed `new()`: no type-name syntax to catch above, so resolve the constructor's
            // containing type (fallback: the target type of the expression). Type-only — a constructor
            // is not a member use (GRAMMAR §4.5).
            case ImplicitObjectCreationExpressionSyntax creation:
                SymbolInfo ctorInfo = model.GetSymbolInfo(creation);
                ISymbol? ctor = ctorInfo.Symbol ?? ctorInfo.CandidateSymbols.FirstOrDefault();
                INamedTypeSymbol? created = (ctor as IMethodSymbol)?.ContainingType
                                            ?? model.GetTypeInfo(creation).Type as INamedTypeSymbol;
                return (created, null);

            default:
                return (null, null);
        }
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