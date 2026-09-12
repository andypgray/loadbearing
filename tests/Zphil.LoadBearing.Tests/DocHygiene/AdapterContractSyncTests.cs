using System.Text.RegularExpressions;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The sync gate over the xUnit adapter's contract, which two surfaces state independently: the
///     public xmldoc on <c>ArchRuleTests</c>, read as IntelliSense by a consumer who has only the
///     package, and the adapter package's README, its landing page on nuget.org. Neither can defer to
///     the other — public xmldoc may not cite a repository document and has to stand alone on hover,
///     and a reader on the package page cannot hover — so the overlap is deliberate, and this gate
///     holds it: a claim rewritten on one surface and left behind on the other fails the suite instead
///     of shipping two contracts under one name.
/// </summary>
/// <remarks>
///     <para>
///         <b>What it cannot do.</b> The other gates in this folder end on an inventory sweep that reds
///         on something the registry never listed. There is no such sweep here, because a claim is a
///         sentence someone decided both surfaces owe the reader and nothing in the text marks one out.
///         The registry below is a floor rather than a census: it holds what it lists, and a claim added
///         to one surface alone is invisible to it. That is the price of covering prose at all, and it
///         is written here so the gate is not read as coverage it does not have.
///     </para>
///     <para>
///         <b>Why containment over normalized text.</b> The two surfaces wrap differently, mark up
///         differently (a <c>c</c> element against a backtick) and escape differently, so one claim's
///         raw bytes never appear in the other. <see cref="Normalize" /> takes both to the same plain
///         prose first — elements to their inner names, entities decoded, markdown marks dropped,
///         whitespace collapsed across newlines. That is why <see cref="QuoteSyncTests" />' fence
///         scanner cannot reach here: it matches one fence line inside one source line, untrimmed.
///     </para>
///     <para>
///         The xmldoc side is read through <see cref="CommentText.Mask" /> over the whole source file
///         rather than the class doc alone, because the de-duplication that shrank this overlap moved
///         several claims down onto the members they belong to. Like its siblings the gate reads only
///         committed bytes: no workspace load, no CLI invocation, so it stays cheap and parallel-safe.
///         It is the first of them to reach a package README.
///     </para>
/// </remarks>
public sealed class AdapterContractSyncTests
{
    private const string XmldocSource = "src/Zphil.LoadBearing.Xunit/ArchRuleTests.cs";

    private const string Readme = "src/Zphil.LoadBearing.Xunit/README.md";

    /// <summary>
    ///     The length each normalized surface must clear. Set well under either surface's real size and
    ///     well over anything a broken read could produce, so a masking or path regression reds here
    ///     rather than passing every containment check against an empty string.
    /// </summary>
    private const int ProseFloor = 1500;

    /// <summary>
    ///     Every claim both surfaces owe the reader, each row carrying the sentence that states it on
    ///     each side. The two spellings differ because the surfaces address different readers; what the
    ///     gate holds is that both still say it.
    /// </summary>
    private static readonly (string Key, string Xmldoc, string Readme)[] SharedClaims =
    [
        ("one-test-per-rule",
            "every rule in the spec runs as its own named test",
            "every rule in an architecture spec runs as an individually named xUnit test"),
        ("rule-id-is-the-display-name",
            "The rule ID is the test's display name",
            "The rule ID is the test's display name"),
        ("failure-carries-the-cli-block",
            "fails with the block loadbearing check prints for it",
            "a failing rule's message is the exact human block the loadbearing CLI prints"),
        ("tripwire-has-no-diff-base",
            "A scope's tripwire is the usual skip: it has no diff base to compare changed files against",
            "A scope's tripwire rule, Quarantine or Caution, reports as skipped (a test run has no diff context)"),
        ("never-builds-and-never-restores",
            "The adapter never builds and never restores",
            "The checker never builds; stale builds give stale verdicts"),
        ("needs-an-sdk-on-the-test-host",
            "It needs a .NET SDK on the test host",
            "A .NET SDK on the test host"),
        ("solution-path-is-the-only-required-override",
            "SolutionPath is the only required override",
            "SolutionPath is the only required override"),
        ("spec-project-leaves-the-universe",
            "along with any project only it pulls in",
            "along with any project only it pulls in"),
        ("incomplete-load-skips-every-rule",
            "every rule case then skips rather than report a verdict reached over a partial model",
            "every rule case skips rather than report a verdict that was never reached"),
        ("opting-in-cannot-vouch-for-the-claim",
            "rather than pass under a name the run cannot vouch for",
            "rather than pass under a name the run cannot vouch for"),
        ("filter-checks-selection-plus-references",
            "checks the projects the filter selects plus everything they reference",
            "checks the projects the filter selects plus everything they reference"),
        ("one-run-per-spec-type",
            "every rule case of that type reads its verdict from that one run",
            "every rule case reads its verdict from that shared run")
    ];

    /// <summary>
    ///     The claims one surface carries on purpose, each with the reason its twin does not. Pinned
    ///     rather than merely written down, so the day a surface grows its counterpart the asymmetry
    ///     reds and the claim is promoted into <see cref="SharedClaims" /> instead of drifting uncovered.
    /// </summary>
    private static readonly (string Key, string Needle, Surface Carrier, string Reason)[] Asymmetries =
    [
        // Consumer-side plumbing a package page has no room for: it answers "do I need one of these?",
        // which only someone already writing the test class asks.
        ("module-initializer-is-registered-for-you",
            "[ModuleInitializer]",
            Surface.Xmldoc,
            "answers a question only a consumer mid-edit asks"),

        // The unreadable-project arm is an edge of one member's behaviour. The README documents the
        // solution-filter arm, which a reader chooses, and leaves this one to the member that reports it.
        ("unreadable-projects-narrow-the-run",
            "a solution declaring projects the checker cannot read",
            Surface.Xmldoc,
            "an edge of one member, not a decision the reader makes"),

        // A Caution scope's tripwire being permanently skipped is a consequence a reader derives from
        // two rules the README already states; spelling it out belongs beside the test that skips.
        ("caution-tripwire-never-fires",
            "always skipped and never fires",
            Surface.Xmldoc,
            "a consequence of two rules the README already states"),

        // Package and runner requirements are install-time, and hover cannot reach a reader still
        // deciding whether to install at all.
        ("runner-and-package-requirements",
            "Microsoft.Testing.Platform",
            Surface.Readme,
            "install-time, and hover comes after installing")
    ];

    private static readonly Regex DocCommentPrefix = new(@"(?m)^[ \t]*///");

    private static readonly Regex CrefElement = new(@"<see\s+cref=""(?:[A-Za-z]:)?([^""]*)""\s*/>");

    private static readonly Regex LangwordElement = new(@"<see\s+langword=""([^""]*)""\s*/>");

    private static readonly Regex ParamrefElement = new(@"<paramref\s+name=""([^""]*)""\s*/>");

    private static readonly Regex AnyElement = new(@"</?[A-Za-z][^>]*>");

    private static readonly Regex MarkdownMarks = new(@"[`*]");

    private static readonly Regex Whitespace = new(@"\s+");

    // Lazy rather than eager, because a field initializer would run before the regexes above are
    // assigned if anyone ever moved these two declarations up.
    private static readonly Lazy<string> XmldocProse =
        new(() => Normalize(CommentText.Mask(RepoRoot.ReadText(XmldocSource))));

    private static readonly Lazy<string> ReadmeProse = new(() => Normalize(RepoRoot.ReadText(Readme)));

    private enum Surface
    {
        Xmldoc,
        Readme
    }

    public static TheoryData<string> SharedClaimKeys => [.. SharedClaims.Select(claim => claim.Key)];

    [Theory]
    [MemberData(nameof(SharedClaimKeys))]
    public void SharedClaim_IsCarriedByBothSurfaces(string key)
    {
        // Arrange
        (string Key, string Xmldoc, string Readme) claim = SharedClaims.Single(candidate => candidate.Key == key);
        List<string> drift = new();

        // Act
        if (!Carries(Surface.Xmldoc, claim.Xmldoc)) drift.Add(Missing(Surface.Xmldoc, claim.Xmldoc));
        if (!Carries(Surface.Readme, claim.Readme)) drift.Add(Missing(Surface.Readme, claim.Readme));

        // Assert
        drift.ShouldReportNothing($"The adapter contract's '{key}' claim has drifted off a surface");
    }

    [Fact]
    public void AsymmetricClaims_StayOnTheOneSurfaceThatOwesThem()
    {
        // Arrange
        List<string> drift = new();

        // Act: each must still be on its carrier — an asymmetry whose text was rewritten is as stale as
        // a shared claim — and must still be absent from the twin, because the day it arrives there the
        // recorded reason has expired and the claim belongs in the shared registry.
        foreach ((string key, string needle, Surface carrier, string reason) in Asymmetries)
        {
            Surface twin = carrier == Surface.Xmldoc ? Surface.Readme : Surface.Xmldoc;

            if (!Carries(carrier, needle)) drift.Add(Missing(carrier, needle));
            if (Carries(twin, needle))
                drift.Add(
                    $"{Name(twin)} now carries '{needle}', held out of the shared registry as {key} because it {reason}; move the row into SharedClaims.");
        }

        // Assert
        drift.ShouldReportNothing("The adapter contract's deliberate asymmetries no longer hold");
    }

    [Fact]
    public void BothSurfaces_YieldProse()
    {
        // Arrange: containment against an empty haystack fails loudly, but containment against an empty
        // needle passes silently, so both ends are measured before anything is searched for.
        List<string> empty = SharedClaims
            .SelectMany(claim => new[] { (claim.Key, Text: claim.Xmldoc), (claim.Key, Text: claim.Readme) })
            .Concat(Asymmetries.Select(asymmetry => (asymmetry.Key, Text: asymmetry.Needle)))
            .Where(registered => Normalize(registered.Text).Length == 0)
            .Select(registered => $"{registered.Key} registers a needle that normalizes to nothing.")
            .ToList();

        // Act & Assert
        XmldocProse.Value.Length.ShouldBeGreaterThan(
            ProseFloor, $"{XmldocSource} yielded almost no comment prose");
        ReadmeProse.Value.Length.ShouldBeGreaterThan(ProseFloor, $"{Readme} yielded almost no prose");
        empty.ShouldReportNothing("These registered needles would match anything");
    }

    private static bool Carries(Surface surface, string claim)
    {
        return Prose(surface)
            .Contains(Normalize(claim), StringComparison.Ordinal);
    }

    private static string Prose(Surface surface)
    {
        return surface == Surface.Xmldoc ? XmldocProse.Value : ReadmeProse.Value;
    }

    private static string Missing(Surface surface, string claim)
    {
        return $"{Name(surface)} no longer carries '{claim}'.";
    }

    private static string Name(Surface surface)
    {
        return surface == Surface.Xmldoc ? XmldocSource : Readme;
    }

    /// <summary>
    ///     Reduces either surface to the same plain prose: masked-out code and doc-comment prefixes
    ///     dropped, documentation elements replaced by the name they point at, remaining markup and
    ///     markdown marks removed, entities decoded, and every whitespace run collapsed to one space.
    /// </summary>
    /// <remarks>
    ///     Two orderings are load-bearing. The mask's blanks become spaces before the <c>///</c> prefixes
    ///     are stripped, because the indentation in front of a doc comment is not itself comment trivia
    ///     and comes back blanked. And elements come out before entities go in, because decoding first
    ///     would turn a written <c>&amp;lt;YourSpec&amp;gt;</c> into something the element pass then eats
    ///     as a tag.
    /// </remarks>
    private static string Normalize(string text)
    {
        string plain = text.Replace(CommentText.Blank, ' ');
        plain = DocCommentPrefix.Replace(plain, string.Empty);
        plain = CrefElement.Replace(plain, "$1");
        plain = LangwordElement.Replace(plain, "$1");
        plain = ParamrefElement.Replace(plain, "$1");
        plain = AnyElement.Replace(plain, string.Empty);
        plain = plain.Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&amp;", "&");
        plain = MarkdownMarks.Replace(plain, string.Empty);

        return Whitespace.Replace(plain, " ")
            .Trim();
    }
}
