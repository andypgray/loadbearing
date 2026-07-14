using Microsoft.CodeAnalysis;
using CoreTypeKind = Zphil.LoadBearing.TypeKind;
using RoslynTypeKind = Microsoft.CodeAnalysis.TypeKind;

namespace Zphil.LoadBearing.Roslyn;

/// <summary>
///     Maps Roslyn's <see cref="RoslynTypeKind" /> onto LoadBearing's Core <see cref="CoreTypeKind" />
///     (the two enums are deliberately distinct — Roslyn must not enter Core). The alias is required
///     because <c>TypeKind</c> is otherwise ambiguous here.
/// </summary>
/// <remarks>
///     Class → Class (records and static classes are classes at the symbol level), Interface,
///     Struct (record structs included), Enum, Delegate. Everything else — Error, TypeParameter,
///     Array, Pointer, Dynamic, Module, Submission, FunctionPointer — is unmappable, so the symbol
///     is skipped entirely by callers.
/// </remarks>
internal static class TypeKindMapper
{
    public static bool TryMap(INamedTypeSymbol symbol, out CoreTypeKind kind)
    {
        switch (symbol.TypeKind)
        {
            case RoslynTypeKind.Class:
                kind = CoreTypeKind.Class;
                return true;
            case RoslynTypeKind.Interface:
                kind = CoreTypeKind.Interface;
                return true;
            case RoslynTypeKind.Struct:
                kind = CoreTypeKind.Struct;
                return true;
            case RoslynTypeKind.Enum:
                kind = CoreTypeKind.Enum;
                return true;
            case RoslynTypeKind.Delegate:
                kind = CoreTypeKind.Delegate;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}