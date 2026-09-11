using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.Extraction;

/// <summary>Query helpers over a <see cref="CodebaseModel" /> to keep extraction assertions terse.</summary>
internal static class ModelQuery
{
    /// <summary>The lines of a list of sites, in recorded order: <c>edge.Sites.Lines()</c>.</summary>
    /// <remarks>
    ///     On the sites rather than on each edge: the eight edge kinds share no sited contract in the shipped
    ///     model, naming the list at the call site says which of an edge's site lists is meant, and adding a
    ///     contract to <c>src/</c> to buy <c>edge.Lines()</c> would put shipped API in the service of a test
    ///     helper.
    /// </remarks>
    public static IReadOnlyList<int> Lines(this IEnumerable<SourceLocation> sites)
    {
        return sites.Select(site => site.Line)
            .ToList();
    }

    /// <summary>The files of a list of sites, in recorded order.</summary>
    public static IReadOnlyList<string> Files(this IEnumerable<SourceLocation> sites)
    {
        return sites.Select(site => site.FilePath)
            .ToList();
    }

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

    public static MemberEdge MemberEdge(this CodebaseModel model, string sourceFullName, string memberSymbolId)
    {
        return model.MemberEdges.Single(e => e.Source.FullName == sourceFullName && e.Member.SymbolId == memberSymbolId);
    }

    public static IReadOnlyList<MemberEdge> MemberEdges(this CodebaseModel model, string sourceFullName)
    {
        return model.MemberEdges.Where(e => e.Source.FullName == sourceFullName)
            .ToList();
    }

    // ── construction edges (GRAMMAR §4.5) ─────────────────────────────────────────────────────────────────

    public static ConstructorEdge ConstructorEdge(this CodebaseModel model, string sourceFullName, string constructedFullName)
    {
        return model.ConstructorEdges.Single(e => e.Source.FullName == sourceFullName && e.Constructed.FullName == constructedFullName);
    }

    public static IReadOnlyList<ConstructorEdge> ConstructorEdges(this CodebaseModel model, string sourceFullName)
    {
        return model.ConstructorEdges.Where(e => e.Source.FullName == sourceFullName)
            .ToList();
    }

    public static bool HasConstructorEdge(this CodebaseModel model, string sourceFullName, string constructedFullName)
    {
        return model.ConstructorEdges.Any(e => e.Source.FullName == sourceFullName && e.Constructed.FullName == constructedFullName);
    }

    public static string FullName(this ITypeInfo type)
    {
        return ((TypeNode)type).FullName;
    }

    // ── injection edges (GRAMMAR §4.7) ────────────────────────────────────────────────────────────────────

    public static InjectionEdge InjectionEdge(this CodebaseModel model, string sourceFullName, string injectedFullName)
    {
        return model.InjectionEdges.Single(e => e.Source.FullName == sourceFullName && e.Injected.FullName == injectedFullName);
    }

    public static IReadOnlyList<InjectionEdge> InjectionEdges(this CodebaseModel model, string sourceFullName)
    {
        return model.InjectionEdges.Where(e => e.Source.FullName == sourceFullName)
            .ToList();
    }

    public static bool HasInjectionEdge(this CodebaseModel model, string sourceFullName, string injectedFullName)
    {
        return model.InjectionEdges.Any(e => e.Source.FullName == sourceFullName && e.Injected.FullName == injectedFullName);
    }

    // ── catch edges (GRAMMAR §4.8) ────────────────────────────────────────────────────────────────────────

    public static CatchEdge CatchEdge(this CodebaseModel model, string sourceFullName, string caughtFullName)
    {
        return model.CatchEdges.Single(e => e.Source.FullName == sourceFullName && e.Caught.FullName == caughtFullName);
    }

    public static IReadOnlyList<CatchEdge> CatchEdges(this CodebaseModel model, string sourceFullName)
    {
        return model.CatchEdges.Where(e => e.Source.FullName == sourceFullName)
            .ToList();
    }

    public static bool HasCatchEdge(this CodebaseModel model, string sourceFullName, string caughtFullName)
    {
        return model.CatchEdges.Any(e => e.Source.FullName == sourceFullName && e.Caught.FullName == caughtFullName);
    }

    // ── throw edges (GRAMMAR §4.8) ────────────────────────────────────────────────────────────────────────

    public static ThrowEdge ThrowEdge(this CodebaseModel model, string sourceFullName, string thrownFullName)
    {
        return model.ThrowEdges.Single(e => e.Source.FullName == sourceFullName && e.Thrown.FullName == thrownFullName);
    }

    public static IReadOnlyList<ThrowEdge> ThrowEdges(this CodebaseModel model, string sourceFullName)
    {
        return model.ThrowEdges.Where(e => e.Source.FullName == sourceFullName)
            .ToList();
    }

    public static bool HasThrowEdge(this CodebaseModel model, string sourceFullName, string thrownFullName)
    {
        return model.ThrowEdges.Any(e => e.Source.FullName == sourceFullName && e.Thrown.FullName == thrownFullName);
    }

    // ── exposure edges (GRAMMAR §4.9) ─────────────────────────────────────────────────────────────────────

    public static ExposureEdge ExposureEdge(this CodebaseModel model, string sourceFullName, string exposedFullName)
    {
        return model.ExposureEdges.Single(e => e.Source.FullName == sourceFullName && e.Exposed.FullName == exposedFullName);
    }

    public static IReadOnlyList<ExposureEdge> ExposureEdges(this CodebaseModel model, string sourceFullName)
    {
        return model.ExposureEdges.Where(e => e.Source.FullName == sourceFullName)
            .ToList();
    }

    public static bool HasExposureEdge(this CodebaseModel model, string sourceFullName, string exposedFullName)
    {
        return model.ExposureEdges.Any(e => e.Source.FullName == sourceFullName && e.Exposed.FullName == exposedFullName);
    }

    // ── registration facts (GRAMMAR §4.7) ─────────────────────────────────────────────────────────────────

    public static ServiceRegistration Registration(
        this CodebaseModel model, Lifetime lifetime, string serviceFullName, string? implementationFullName)
    {
        return model.ServiceRegistrations.Single(r => r.Lifetime == lifetime && r.ServiceFullName == serviceFullName && r.ImplementationFullName == implementationFullName);
    }

    public static bool HasRegistration(
        this CodebaseModel model, Lifetime lifetime, string serviceFullName, string? implementationFullName)
    {
        return model.ServiceRegistrations.Any(r => r.Lifetime == lifetime && r.ServiceFullName == serviceFullName && r.ImplementationFullName == implementationFullName);
    }

    // ── declared members (GRAMMAR §4.6) ───────────────────────────────────────────────────────────────────

    public static MemberNode Member(this TypeNode type, string symbolId)
    {
        return type.Members.Single(m => m.SymbolId == symbolId);
    }

    public static MemberNode Member(this CodebaseModel model, string typeFullName, string symbolId)
    {
        return model.Type(typeFullName)
            .Member(symbolId);
    }

    /// <summary>The type's declared-member SymbolIds, in the model's ordinal-by-SymbolId order.</summary>
    public static IReadOnlyList<string> MemberIds(this TypeNode type)
    {
        return type.Members.Select(m => m.SymbolId)
            .ToList();
    }

    /// <summary>
    ///     The member's declared attributes as (definition, constructed) name pairs, in recorded order — so a
    ///     generic attribute's divergence is assertable as one value.
    /// </summary>
    public static IReadOnlyList<(string Definition, string Constructed)> AttributeNames(this MemberNode member)
    {
        return member.Attributes.Select(a => (a.DefinitionFullName, a.FullName))
            .ToList();
    }
}
