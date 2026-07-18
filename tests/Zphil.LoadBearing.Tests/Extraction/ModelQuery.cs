using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>Query helpers over a <see cref="CodebaseModel" /> to keep extraction assertions terse.</summary>
internal static class ModelQuery
{
    public static TypeNode Type(this CodebaseModel model, string fullName)
    {
        return model.Types.Single(t => t.FullName == fullName);
    }

    public static ReferenceEdge Edge(this CodebaseModel model, string sourceFullName, string targetFullName)
    {
        return model.Edges.Single(e => e.Source.FullName == sourceFullName && e.Target.FullName == targetFullName);
    }

    public static bool HasEdge(this CodebaseModel model, string sourceFullName, string targetFullName)
    {
        return model.Edges.Any(e => e.Source.FullName == sourceFullName && e.Target.FullName == targetFullName);
    }

    public static IReadOnlyList<int> Lines(this ReferenceEdge edge)
    {
        return edge.Sites.Select(s => s.Line).ToList();
    }

    public static IReadOnlyList<string> Files(this ReferenceEdge edge)
    {
        return edge.Sites.Select(s => s.FilePath).ToList();
    }

    public static MemberEdge MemberEdge(this CodebaseModel model, string sourceFullName, string memberSymbolId)
    {
        return model.MemberEdges.Single(e => e.Source.FullName == sourceFullName && e.Member.SymbolId == memberSymbolId);
    }

    public static IReadOnlyList<MemberEdge> MemberEdges(this CodebaseModel model, string sourceFullName)
    {
        return model.MemberEdges.Where(e => e.Source.FullName == sourceFullName).ToList();
    }

    public static bool HasMemberEdge(this CodebaseModel model, string sourceFullName, string memberSymbolId)
    {
        return model.MemberEdges.Any(e => e.Source.FullName == sourceFullName && e.Member.SymbolId == memberSymbolId);
    }

    public static IReadOnlyList<int> Lines(this MemberEdge edge)
    {
        return edge.Sites.Select(s => s.Line).ToList();
    }

    public static string FullName(this ITypeInfo type)
    {
        return ((TypeNode)type).FullName;
    }

    // ── declared members (GRAMMAR §4.6) ───────────────────────────────────────────────────────────────────

    public static MemberNode Member(this TypeNode type, string symbolId)
    {
        return type.Members.Single(m => m.SymbolId == symbolId);
    }

    public static MemberNode Member(this CodebaseModel model, string typeFullName, string symbolId)
    {
        return model.Type(typeFullName).Member(symbolId);
    }

    /// <summary>The type's declared-member SymbolIds, in the model's ordinal-by-SymbolId order.</summary>
    public static IReadOnlyList<string> MemberIds(this TypeNode type)
    {
        return type.Members.Select(m => m.SymbolId).ToList();
    }

    public static IReadOnlyList<int> DeclarationLines(this MemberNode member)
    {
        return member.DeclarationSites.Select(s => s.Line).ToList();
    }
}