using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Walks a single type-declaration part and yields the types its source binds a name to — as a
///     type (base lists, signatures, generic args, <c>typeof</c>, casts, creations, static receivers,
///     attribute names, <c>nameof</c> operands) or via any member of it (invocations, property/field/
///     event access, extension-method calls). This is the operational meaning of GRAMMAR §4.1's
///     "a source-level type reference" (ratified for Phase 2).
/// </summary>
/// <remarks>
///     Nested type declarations are a hard boundary: <c>descendIntoChildren</c> refuses to descend
///     into a <see cref="BaseTypeDeclarationSyntax" />/<see cref="DelegateDeclarationSyntax" /> other
///     than the root, so a nested type's references belong to the nested node (walked independently)
///     and file-level <c>using</c> directives — which sit outside any type — are never attributed to
///     a type. Targets are normalized to their <c>OriginalDefinition</c> and gated by
///     <see cref="TypeKindMapper" />; a repeated bind on one line yields duplicate tuples that the
///     caller dedupes by (file, line).
/// </remarks>
internal static class ReferenceWalker
{
    public static IEnumerable<(INamedTypeSymbol Target, string File, int Line)> Walk(SyntaxNode root, SemanticModel model)
    {
        foreach (SyntaxNode node in root.DescendantNodes(n => ReferenceEquals(n, root)
                                                              || n is not (BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)))
        {
            INamedTypeSymbol? target = ResolveTarget(node, model);
            if (target is null) continue;

            var definition = (INamedTypeSymbol)target.OriginalDefinition;
            if (definition.IsImplicitlyDeclared || !definition.CanBeReferencedByName) continue;

            if (!TypeKindMapper.TryMap(definition, out _)) continue;

            int line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            yield return (definition, node.SyntaxTree.FilePath, line);
        }
    }

    /// <summary>
    ///     Resolves a syntax node to the type it binds a name to, or <see langword="null" /> when the
    ///     node is not a reference (namespaces, type parameters, locals/params, discards, dynamic,
    ///     arrays/pointers, predefined-type keywords, and unresolved names all fall through).
    /// </summary>
    private static INamedTypeSymbol? ResolveTarget(SyntaxNode node, SemanticModel model)
    {
        switch (node)
        {
            // IdentifierNameSyntax (excluding the `var` contextual keyword) and GenericNameSyntax.
            // Covers type references and member references (invocations, property/field/event access,
            // static receivers, attribute names, method groups, extension methods, nameof operands).
            case SimpleNameSyntax name when name is not IdentifierNameSyntax { IsVar: true }:
                SymbolInfo info = model.GetSymbolInfo(name);
                ISymbol? symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
                return ContainingTypeOf(symbol);

            // Target-typed `new()`: no type-name syntax to catch above, so resolve the constructor's
            // containing type (fallback: the target type of the expression).
            case ImplicitObjectCreationExpressionSyntax creation:
                SymbolInfo ctorInfo = model.GetSymbolInfo(creation);
                ISymbol? ctor = ctorInfo.Symbol ?? ctorInfo.CandidateSymbols.FirstOrDefault();
                return (ctor as IMethodSymbol)?.ContainingType
                       ?? model.GetTypeInfo(creation).Type as INamedTypeSymbol;

            default:
                return null;
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
}