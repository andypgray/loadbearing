using System.Xml.Linq;
using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The gate that keeps the solution <c>.DotSettings</c> well-formed XML, its silent-cleanup
///     default naming a profile the file actually defines, that profile keeping the two cleanup
///     tasks off whose <c>False</c> is load-bearing, line wrapping declared off with no
///     chain-wrapping key left standing beside it, the var policy pinned evident-only for
///     all three declaration categories with the four severities that make it enforceable, and the
///     async-overload rule promoted solution-wide while the test project's own settings layer scopes
///     it off — that layer well-formed too, and the seven solution-wide usage rules declared rather
///     than left unset.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why well-formedness needs a gate.</b> ReSharper's own settings reader tolerates
///         malformed XML, so jb keeps working — named profile, severities and all — after the file
///         stops being readable by any conformant XML parser. The consumers that break are the
///         strict ones: a runner that resolves the silent-cleanup default with
///         <see cref="XDocument.Load(string)" /> falls back on failure to
///         <c>Built-in: Full Cleanup</c>, which rewrites the deliberate named arguments GRAMMAR §10
///         makes documentation. That split shipped once — a comment edit introduced the adjacent
///         hyphens XML forbids inside comments — and nothing went red for five days, because the
///         fallback surfaces in the consumer's log rather than in any gate.
///     </para>
///     <para>
///         <b>Why the profile's content is pinned too.</b> The declared default and the profile it
///         names sit at opposite ends of the file and drift independently; a rename that reaches
///         only one end sends every profile-less cleanup to Full Cleanup just as silently. And of
///         the profile's tasks, two are off for cause rather than taste: ArrangeArgumentsStyle, the
///         task that would normalise named arguments to positional, and CSReorderTypeMembers, the
///         only task that moves members wholesale across line-pinned files. The blob is not pinned
///         whole — every other task is a nicety, adjustable without ceremony.
///     </para>
///     <para>
///         <b>Why wrapping is pinned from both sides.</b> The harness's fluent chains are chopped one
///         call per line, and the formatter's job is to preserve that shape, never to compute it.
///         Two different edits break that, in opposite directions. Deleting the <c>WRAP_LINES</c>
///         entry looks like removing a restriction and is really the opposite — the ReSharper default
///         is wrapping on, which fully chops any chain already carrying a break. And leaving a
///         <c>WRAP_CHAINED_METHOD_CALLS</c> key behind after the one-time chopping pass arms every
///         later cleanup to chop the files that pass deliberately left alone. Both show up as an
///         enormous diff on some unrelated file's next cleanup, long after the edit that caused it.
///     </para>
///     <para>
///         <b>Why the var policy is pinned.</b> Until 2026-08-14 the evident-only var style rode
///         in from this machine's personal global settings layer, which jb reads beneath the
///         solution file: two categories were set there, and the third ran at its factory
///         always-var default. A policy the repo does not carry disappears on any other machine —
///         and cleanup then rewrites every file it touches toward whatever that machine declares.
///         The three entries pin the policy in the file cleanup actually reads.
///     </para>
///     <para>
///         <b>Why the severities are pinned beside them.</b> The style entries decide what cleanup
///         writes; they say nothing until someone runs it. The four <c>SuggestVarOrType</c>
///         severities are what make a departure visible on its own — at <c>WARNING</c> the
///         zero-at-Warning inspect baseline fails on the drift, where ReSharper's default of
///         suggestion sits silently below the level the baseline reads. Delete them and the policy
///         is still declared and still applied by cleanup, but nothing reports a file that has not
///         been cleaned since it drifted, which is the state this repo spent three hundred
///         declarations getting out of.
///     </para>
///     <para>
///         <b>Why a project layer needs its own well-formedness gate.</b> The test project carries the
///         repo's first per-project settings file, and its failure mode is quieter than the solution
///         file's. A malformed solution file at least keeps working for jb; a malformed project layer
///         is mounted by nobody, reported by nothing, and every inspection it scopes silently keeps
///         its solution-level severity. That is indistinguishable from the layer mechanism not
///         existing — it cost a probe here, to adjacent hyphens introduced by spelling a jb switch
///         literally inside a comment, which is the same edit that once broke the solution file.
///     </para>
///     <para>
///         <b>Why the seven usage severities are pinned at their own default.</b> They set
///         <c>SUGGESTION</c>, which is what they would inherit anyway, so they change no behaviour by
///         design. What they change is the record: a rule decided by solution-wide search cannot be
///         answered by a solution that ships four public packages, and saying so in the file turns a
///         standing decision into something a later edit has to argue with rather than merely omit.
///         The <c>_002E</c> spelling is load-bearing — it is the escape for the dot in a rule id, and
///         a literal dot yields a key ReSharper never reads, with nothing anywhere reporting it.
///     </para>
/// </remarks>
public sealed class DotSettingsCleanupProfileTests
{
    private const string SilentCleanupProfileKey = "/Default/CodeStyle/CodeCleanup/SilentCleanupProfile/@EntryValue";
    private const string ProfileKeyPrefix = "/Default/CodeStyle/CodeCleanup/Profiles/=";
    private const string WrapLinesKey = "/Default/CodeStyle/CodeFormatting/CSharpFormat/WRAP_LINES/@EntryValue";
    private const string ChainWrapKeyFragment = "WRAP_CHAINED_METHOD_CALLS";
    private const string VarKeywordUsageKeyPrefix = "/Default/CodeStyle/CSharpVarKeywordUsage/";
    private const string InspectionSeverityKeyPrefix = "/Default/CodeInspection/Highlighting/InspectionSeverities/=";

    private static readonly XName KeyName = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");

    [Fact]
    public void SolutionDotSettings_IsWellFormedXml()
    {
        Should.NotThrow(
            static () => XDocument.Load(RepoRoot.SolutionDotSettings),
            "ReSharper tolerates a malformed .DotSettings, so nothing else in the toolchain notices "
            + "when this file stops parsing — strict readers, silent-cleanup profile resolution "
            + "among them, silently fall back to Built-in: Full Cleanup, which strips "
            + "deliberate named arguments. The classic way to break it: two adjacent hyphens inside "
            + "an XML comment.");
    }

    [Fact]
    public void SilentCleanupProfile_NamesAProfileTheFileDefines()
    {
        // Arrange
        XDocument settings = XDocument.Load(RepoRoot.SolutionDotSettings);
        string declared = DeclaredProfileName(settings);

        // Act
        IReadOnlyList<string?> defined = DefinedProfileNames(settings);

        // Assert
        defined.ShouldContain(declared,
            "The silent-cleanup default and the profile it names sit at opposite ends of the file; "
            + "a rename that reaches only one end sends every profile-less cleanup to Built-in: "
            + "Full Cleanup, silently.");
    }

    [Fact]
    public void DeclaredCleanupProfile_KeepsArgumentStyleAndMemberOrderAsAuthored()
    {
        // Arrange
        XDocument settings = XDocument.Load(RepoRoot.SolutionDotSettings);
        XElement profile = DeclaredProfile(settings);

        // Act
        string? arrangeArgumentsStyle = profile.Element("CSCodeStyleAttributes")
            ?.Attribute("ArrangeArgumentsStyle")
            ?.Value;
        string? reorderTypeMembers = profile.Element("CSReorderTypeMembers")
            ?.Value;

        // Assert
        arrangeArgumentsStyle.ShouldBe("False",
            "ArrangeArgumentsStyle is the cleanup task that normalises arguments to positional; False "
            + "is the only value that leaves the deliberate named arguments (GRAMMAR §10) as-authored.");
        reorderTypeMembers.ShouldBe("False",
            "CSReorderTypeMembers is the only cleanup task that moves a member wholesale across a "
            + "file, and SpecValidationSpecs.cs pins expected messages to literal line numbers; False "
            + "is what lets cleanup run over any file in the repo without consulting a list.");
    }

    [Fact]
    public void LineWrapping_IsDeclaredOffRatherThanLeftUnset()
    {
        // Arrange
        XDocument settings = XDocument.Load(RepoRoot.SolutionDotSettings);

        // Act
        XElement? wrapLines = ShouldHaveEntries(settings)
            .SingleOrDefault(static entry => KeyOf(entry) == WrapLinesKey);

        // Assert
        XElement declared = wrapLines.ShouldNotBeNull(
            "Wrapping off is not ReSharper's default, so deleting this entry re-enables margin "
            + "wrapping rather than leaving things as they are — and margin wrapping fully chops any "
            + "chain that already carries one break, which is most of the line-pinned ones.");
        declared.Value.ShouldBe("False",
            "The chopped chains throughout the harness are preserved, never produced, by the "
            + "formatter; any value but False puts cleanup back in the business of rewriting them.");
    }

    [Fact]
    public void ChainMethodWrapping_HasNoEntryLeftBehind()
    {
        // Arrange
        XDocument settings = XDocument.Load(RepoRoot.SolutionDotSettings);

        // Act
        string[] chainWrapKeys = ShouldHaveEntries(settings)
            .Select(KeyOf)
            .Where(static key => key.Contains(ChainWrapKeyFragment, StringComparison.Ordinal))
            .ToArray();

        // Assert
        chainWrapKeys.ShouldBeEmpty(
            "CHOP_ALWAYS belongs to the one-time pass that chopped the harness and was removed with "
            + "it. Left standing, it chops every two-call chain on the next cleanup of any file — "
            + "SpecValidationSpecs.cs included, whose forty expected messages quote literal line "
            + "numbers.");
    }

    [Fact]
    public void VarKeywordUsage_PinsEvidentOnlyVarForAllThreeCategories()
    {
        // Arrange
        XDocument settings = XDocument.Load(RepoRoot.SolutionDotSettings);

        // Act
        string? forBuiltInTypes = VarUsageValueFor(settings, "ForBuiltInTypes");
        string? forSimpleTypes = VarUsageValueFor(settings, "ForSimpleTypes");
        string? forOtherTypes = VarUsageValueFor(settings, "ForOtherTypes");

        // Assert
        forBuiltInTypes.ShouldBe("UseVarWhenEvident",
            "Built-in types took evident-only var from this machine's personal global settings "
            + "layer until 2026-08-14; the repo's own entry is what holds string s = Method() "
            + "explicit on a machine whose global layer says use-var.");
        forSimpleTypes.ShouldBe("UseVarWhenEvident",
            "Simple types rode the same personal global layer as built-ins; without this entry "
            + "the category silently follows whichever machine runs cleanup.");
        forOtherTypes.ShouldBe("UseVarWhenEvident",
            "The entry that closes the original gap: ForOtherTypes' factory default is "
            + "always-var, which kept var on generic-returning calls where nothing on the line "
            + "names the type; deleting it reverts every such site on the next cleanup of its "
            + "file.");
    }

    [Fact]
    public void SuggestVarOrTypeSeverities_ArePromotedToWarning()
    {
        // Arrange
        XDocument settings = XDocument.Load(RepoRoot.SolutionDotSettings);

        // Act
        string? builtInTypes = SeverityValueFor(settings, "SuggestVarOrType_BuiltInTypes");
        string? simpleTypes = SeverityValueFor(settings, "SuggestVarOrType_SimpleTypes");
        string? elsewhere = SeverityValueFor(settings, "SuggestVarOrType_Elsewhere");
        string? deconstructionDeclarations = SeverityValueFor(settings, "SuggestVarOrType_DeconstructionDeclarations");

        // Assert
        builtInTypes.ShouldBe("WARNING",
            "The var style entries decide what cleanup writes and say nothing until someone runs "
            + "it; this severity is what makes a drifted declaration visible on its own.");
        simpleTypes.ShouldBe("WARNING",
            "Same reason as the built-in types entry: the pinned style is only enforceable while "
            + "inspect reports a departure from it.");
        elsewhere.ShouldBe("WARNING",
            "The category covering ForOtherTypes, which is where the original always-var gap lived "
            + "— the one most likely to drift back.");
        deconstructionDeclarations.ShouldBe("WARNING",
            "Promoted with the other three although no ForDeconstructionDeclarations style key "
            + "sits beside it: the declarations in the tree are already explicit, so this holds "
            + "the line where it is. Should it ever red, pin the missing style key rather than "
            + "demoting this entry.");
    }

    [Fact]
    public void ProjectSettingsLayer_IsWellFormedXml()
    {
        Should.NotThrow(
            static () => XDocument.Load(RepoRoot.TestProjectDotSettings),
            "A malformed project layer fails differently from a malformed solution file, and worse: "
            + "jb mounts nothing and says nothing, so every inspection the layer scopes quietly keeps "
            + "its solution-level severity. It reads exactly like the layer mechanism not working, "
            + "and it cost a probe here before this gate existed. Adjacent hyphens inside an XML "
            + "comment are the way in — spelling a jb switch literally is enough to do it.");
    }

    [Fact]
    public void AsyncOverloadSeverity_IsPromotedSolutionWideAndScopedOffInTheTestProject()
    {
        // Arrange
        XDocument solution = XDocument.Load(RepoRoot.SolutionDotSettings);
        XDocument testProject = XDocument.Load(RepoRoot.TestProjectDotSettings);

        // Act
        string? solutionWide = SeverityValueFor(solution, "MethodHasAsyncOverload");
        string? inTestProject = SeverityValueFor(testProject, "MethodHasAsyncOverload");

        // Assert
        solutionWide.ShouldBe("WARNING",
            "ReSharper raises this one only from inside an async method, so it never fires on "
            + "deliberately synchronous code — at WARNING it says what Core, Roslyn, Xunit and the "
            + "CLI mean by being async: already async here, do not block. Demote it and a sync call "
            + "added to one of those pipelines reports nowhere.");
        inTestProject.ShouldBe("DO_NOT_SHOW",
            "Every finding it raises in the test project is fixture file IO in an arrange block, "
            + "where awaiting yields no thread and saves no blocking. Scoping it off in the project "
            + "layer rather than demoting it above is what keeps the guard live everywhere else — "
            + "and the layer only outranks the solution file while jb mounts it, which is why the "
            + "well-formedness gate above sits beside this one.");
    }

    [Theory]
    [InlineData("RedundantTypeArgumentsInsideNameof")]
    [InlineData("ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract")]
    [InlineData("CanSimplifyDictionaryTryGetValueWithGetValueOrDefault")]
    [InlineData("CanSimplifyDictionaryLookupWithTryAdd")]
    [InlineData("ChangeFieldTypeToSystemThreadingLock")]
    [InlineData("MemberCanBePrivate_002ELocal")]
    [InlineData("ClassNeverInstantiated_002ELocal")]
    public void LocallyDecidableInspections_ArePromotedToWarning(string inspection)
    {
        // Arrange
        XDocument settings = XDocument.Load(RepoRoot.SolutionDotSettings);

        // Act
        string? severity = SeverityValueFor(settings, inspection);

        // Assert
        severity.ShouldBe("WARNING",
            $"{inspection} is answerable from evidence inside this tree — a scope that closes at one "
            + "file, or an equivalence the language itself settles — so a new site is drift rather "
            + "than a judgement call. Its sites were cleared before this promotion, in that order: "
            + "promoting first would have broken the zero-at-Warning baseline instead of holding it.");
    }

    [Theory]
    [InlineData("ForCanBeConvertedToForeach")]
    [InlineData("ConvertToLocalFunction")]
    [InlineData("ConvertToPrimaryConstructor")]
    [InlineData("ReplaceWithFieldKeyword")]
    [InlineData("IntroduceOptionalParameters_002EGlobal")]
    [InlineData("IntroduceOptionalParameters_002ELocal")]
    [InlineData("UseObjectOrCollectionInitializer")]
    [InlineData("RedundantVerbatimStringPrefix")]
    public void WeighedAndDeclinedInspections_AreHeldAtSuggestion(string inspection)
    {
        // Arrange
        XDocument settings = XDocument.Load(RepoRoot.SolutionDotSettings);

        // Act
        string? severity = SeverityValueFor(settings, inspection);

        // Assert
        severity.ShouldBe("SUGGESTION",
            $"{inspection} asks for a rewrite that costs something real in this tree — a capture moved, "
            + "a public overload pair reshaped, an index walk that runs once per type. Holding it at "
            + "its inherited value keeps it visible in the IDE for a per-site judgement while recording "
            + "that the tree-wide answer was reached rather than skipped. The file says which reason "
            + "belongs to which rule.");
    }

    [Theory]
    [InlineData("MemberCanBePrivate_002EGlobal")]
    [InlineData("UnusedType_002EGlobal")]
    [InlineData("UnusedMember_002EGlobal")]
    [InlineData("UnusedMethodReturnValue_002EGlobal")]
    [InlineData("UnusedMemberInSuper_002EGlobal")]
    [InlineData("UnusedParameter_002EGlobal")]
    [InlineData("ClassNeverInstantiated_002EGlobal")]
    public void SolutionWideUsageSeverities_AreDeclaredAtSuggestionRatherThanLeftUnset(string inspection)
    {
        // Arrange
        XDocument settings = XDocument.Load(RepoRoot.SolutionDotSettings);

        // Act
        string? severity = SeverityValueFor(settings, inspection);

        // Assert
        severity.ShouldBe("SUGGESTION",
            $"{inspection} decides its finding by solution-wide search, and this solution ships four "
            + "public packages, so the consumers that would answer it sit outside the search. "
            + "SUGGESTION is already the inherited default, which is exactly the point: the entry "
            + "records a reviewed decision where an absence records nothing, and it makes a later "
            + "promotion or DO_NOT_SHOW a visible edit rather than a silent one. The _002E is the "
            + "escape for the dot in the rule id — spell the dot literally and the key becomes one "
            + "ReSharper never reads, with no error to say so.");
    }

    private static string DeclaredProfileName(XDocument settings)
    {
        XElement entry = ShouldHaveEntries(settings)
            .Single(static entry => KeyOf(entry) == SilentCleanupProfileKey);

        return entry.Value;
    }

    private static XElement DeclaredProfile(XDocument settings)
    {
        string declared = DeclaredProfileName(settings);

        return ShouldHaveDefinedProfiles(settings)
            .Single(profile => profile.Attribute("name")
                ?.Value == declared);
    }

    private static IReadOnlyList<string?> DefinedProfileNames(XDocument settings)
    {
        return ShouldHaveDefinedProfiles(settings)
            .Select(static profile => profile.Attribute("name")
                ?.Value)
            .ToArray();
    }

    // Each profile is a whole XML document escaped into its entry's text; parsing it back out reads
    // the content the way ReSharper does, so the pins hold against what actually runs.
    private static IReadOnlyList<XElement> ShouldHaveDefinedProfiles(XDocument settings)
    {
        return ShouldHaveEntries(settings)
            .Where(static entry => KeyOf(entry)
                .StartsWith(ProfileKeyPrefix, StringComparison.Ordinal))
            .Select(static entry => XDocument.Parse(entry.Value)
                .Root.ShouldNotBeNull())
            .ToArray();
    }

    private static IEnumerable<XElement> ShouldHaveEntries(XDocument settings)
    {
        XElement root = settings.Root.ShouldNotBeNull();

        return root.Elements();
    }

    private static string KeyOf(XElement entry)
    {
        return entry.Attribute(KeyName)
            ?.Value ?? "";
    }

    private static string? VarUsageValueFor(XDocument settings, string category)
    {
        string key = VarKeywordUsageKeyPrefix + category + "/@EntryValue";

        return ShouldHaveEntries(settings)
            .SingleOrDefault(entry => KeyOf(entry) == key)
            ?.Value;
    }

    private static string? SeverityValueFor(XDocument settings, string inspection)
    {
        string key = InspectionSeverityKeyPrefix + inspection + "/@EntryIndexedValue";

        return ShouldHaveEntries(settings)
            .SingleOrDefault(entry => KeyOf(entry) == key)
            ?.Value;
    }
}
