using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;

namespace Zphil.LoadBearing.Tests.TestSupport;

/// <summary>
///     Shallow codebase nodes for the rows whose subject is a shape — an identity, a count, a rendered line —
///     rather than any real code: the caller steers the full name, the symbol ID, the declaring project and
///     the declaration sites, and every other scalar fact is an inert placeholder nothing under test reads. The
///     stand-in violations those same rows count are held here for the same reason.
/// </summary>
/// <remarks>
///     One spelling of the thirteen-argument <see cref="TypeNode" /> constructor, so a slot added to it is
///     one edit here rather than one per test file that used to carry its own copy.
/// </remarks>
internal static class SyntheticNodes
{
    /// <summary>
    ///     A type whose symbol ID is <c>T:</c> + its full name, declared at <paramref name="sites" /> — none
    ///     by default.
    /// </summary>
    internal static TypeNode Type(string fullName, params SourceLocation[] sites)
    {
        return Type(fullName, "T:" + fullName, "TestProject", sites);
    }

    /// <summary>
    ///     A type carrying the symbol ID and declaring project given — for the rows about identity and
    ///     attribution, where the ID is not derivable from the name or the project is what varies.
    /// </summary>
    internal static TypeNode Type(string fullName, string symbolId, string project = "TestProject")
    {
        return Type(fullName, symbolId, project, Array.Empty<SourceLocation>());
    }

    /// <summary>
    ///     <paramref name="count" /> distinct <c>file:line</c> sites in one file, lines 1 through
    ///     <paramref name="count" />.
    /// </summary>
    internal static IReadOnlyList<SourceLocation> Sites(string file, int count)
    {
        return Enumerable.Range(1, count)
            .Select(line => new SourceLocation(file, line))
            .ToList();
    }

    /// <summary>
    ///     <paramref name="count" /> stand-in violations carrying no site at all, which is what makes
    ///     them the right default: the site total prints only when it exceeds the pair count, so a rule
    ///     built from these prints the line it printed before the measure existed.
    /// </summary>
    internal static IReadOnlyList<Violation> RuleErrors(int count)
    {
        return Enumerable.Range(0, count)
            .Select(_ => Violation.RuleError("x"))
            .ToList();
    }

    private static TypeNode Type(string fullName, string symbolId, string project, IReadOnlyList<SourceLocation> sites)
    {
        return new TypeNode(
            fullName, symbolId, fullName, string.Empty, TypeKind.Class, Accessibility.Public,
            false, false, false, false, false, project, false) { DeclarationSites = sites };
    }
}
