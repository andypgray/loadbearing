using System.Collections.Concurrent;
using Shouldly;
using Xunit;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     Unit tests for <see cref="DocGate{TQuote}" /> and <see cref="DocGateAssertions" />. They pin the
///     text the harness composes — the emptiness header under its defaults and under each override, the
///     registry sweep's finding and header, and the collector's join — so the gate and these tests
///     exercise the same code path.
/// </summary>
/// <remarks>
///     The wording is the subject rather than a detail of it. Every gate on this harness rests on the
///     promise that the failure message a reader already knows stays byte-identical, and a composed
///     message is exactly the kind of thing that survives review once and drifts afterwards. Each fact
///     below therefore forces a real red and reads the message out of it, rather than asserting on the
///     three nouns it was built from.
/// </remarks>
public sealed class DocGateTests
{
    /// <summary>
    ///     A doc git certainly tracks, so it is in the set the registry sweep reads and can stand in for
    ///     both a registered doc and an unregistered one.
    /// </summary>
    private const string TrackedDoc = "README.md";

    [Fact]
    public void EmptinessGuard_UnderItsDefaults_NamesTheQuotesAndTheEmptyDoc()
    {
        // Arrange: a registered doc the scanner finds nothing in.
        DocGate<int> gate = GateOver([TrackedDoc], static _ => 0, "rule quotes");

        // Act
        var failure =
            Should.Throw<ShouldAssertException>(gate.ShouldYieldFromEveryRegisteredDoc);

        // Assert
        failure.Message.ShouldContain(
            $"These docs yielded no rule quotes; the scanner may be silently matching nothing:\n{TrackedDoc}");
    }

    [Fact]
    public void EmptinessGuard_WithADocSetNoun_NamesTheDocsAsThatKind()
    {
        // Arrange
        DocGate<int> gate = GateOver([TrackedDoc], static _ => 0, "anchors", docs: "anchor docs");

        // Act
        var failure =
            Should.Throw<ShouldAssertException>(gate.ShouldYieldFromEveryRegisteredDoc);

        // Assert
        failure.Message.ShouldContain(
            $"These anchor docs yielded no anchors; the scanner may be silently matching nothing:\n{TrackedDoc}");
    }

    [Fact]
    public void EmptinessGuard_WithAScannerName_NamesThatScanner()
    {
        // Arrange
        DocGate<int> gate =
            GateOver([TrackedDoc], static _ => 0, "fenced code blocks", scanner: "fence scanner");

        // Act
        var failure =
            Should.Throw<ShouldAssertException>(gate.ShouldYieldFromEveryRegisteredDoc);

        // Assert
        failure.Message.ShouldContain(
            $"These docs yielded no fenced code blocks; the fence scanner may be silently matching nothing:\n{TrackedDoc}");
    }

    [Fact]
    public void EmptinessGuard_WhenEveryRegisteredDocYields_Passes()
    {
        // Arrange
        DocGate<int> gate = GateOver([TrackedDoc], static _ => 1, "rule quotes");

        // Act & Assert
        Should.NotThrow(gate.ShouldYieldFromEveryRegisteredDoc);
    }

    [Fact]
    public void RegistrySweep_WhenAnUnregisteredDocYields_CountsItAndNamesTheAuthority()
    {
        // Arrange: nothing registered, and a tracked doc the scanner finds two quotes in.
        DocGate<int> gate = GateOver([], static doc => doc == TrackedDoc ? 2 : 0, "rule quotes");

        // Act
        var failure = Should.Throw<ShouldAssertException>(() =>
            gate.ShouldFindNothingOutsideTheRegistry(
                counted: "rule sentence(s)",
                swept: "rule sentences",
                authority: "the spec"));

        // Assert
        failure.Message.ShouldContain(
            "These tracked docs quote rule sentences that nothing holds to the spec:\n"
            + $"{TrackedDoc} quotes 2 rule sentence(s) but is not registered.");
    }

    [Fact]
    public void RegistrySweep_WhenTheOnlyYieldingDocIsRegistered_Passes()
    {
        // Arrange
        DocGate<int> gate = GateOver([TrackedDoc], static doc => doc == TrackedDoc ? 2 : 0, "rule quotes");

        // Act & Assert
        Should.NotThrow(() =>
            gate.ShouldFindNothingOutsideTheRegistry(
                counted: "rule sentence(s)",
                swept: "rule sentences",
                authority: "the spec"));
    }

    [Fact]
    public void Scan_ReadsEachDocOnce_HoweverManyFactsAskForIt()
    {
        // Arrange: the memoization is what lets a gate sweep every tracked doc and then re-read its own
        // registered ones without paying for either twice.
        ConcurrentDictionary<string, int> reads = new(StringComparer.Ordinal);
        DocGate<int> gate = new(
            [TrackedDoc],
            doc =>
            {
                reads.AddOrUpdate(doc, 1, static (_, count) => count + 1);
                return [1];
            },
            "rule quotes");

        // Act
        gate.Scan(TrackedDoc);
        gate.Scan(TrackedDoc);
        gate.ShouldYieldFromEveryRegisteredDoc();

        // Assert
        reads[TrackedDoc]
            .ShouldBe(1);
    }

    [Fact]
    public void ShouldReportNothing_WhenFindingsExist_JoinsThemUnderTheHeader()
    {
        // Arrange
        string[] findings = ["first finding", "second finding"];

        // Act: the call sits on its own line, as it does at every gate, so the message names the list.
        var failure = Should.Throw<ShouldAssertException>(() =>
            findings.ShouldReportNothing("Something has drifted"));

        // Assert
        failure.Message.ShouldContain("Something has drifted:\nfirst finding\nsecond finding");
    }

    [Fact]
    public void ShouldReportNothing_NamesTheCallersList_NotItsOwnParameter()
    {
        // Arrange: what the [ShouldlyMethods] attribution on DocGateAssertions buys, pinned rather than
        // argued. Take the attribute off and Shouldly reads the frame inside that file instead, so every
        // gate's red opens with the helper's parameter name — the same word for all forty-five of them.
        string[] drift = ["a drifted quote"];

        // Act
        var failure = Should.Throw<ShouldAssertException>(() =>
            drift.ShouldReportNothing("Something has drifted"));

        // Assert
        failure.Message.ShouldStartWith("drift");
    }

    [Fact]
    public void ShouldReportNothing_WhenThereAreNoFindings_Passes()
    {
        // Act & Assert
        Should.NotThrow(() => Array.Empty<string>()
            .ShouldReportNothing("Something has drifted"));
    }

    // A gate whose scanner yields as many placeholder quotes as the count says, which is all either
    // shared fact reads of what it scanned.
    private static DocGate<int> GateOver(
        IReadOnlyList<string> registeredDocs,
        Func<string, int> countFor,
        string quotes,
        string docs = "docs",
        string scanner = "scanner")
    {
        return new DocGate<int>(
            registeredDocs,
            doc => Enumerable.Range(0, countFor(doc))
                .ToArray(),
            quotes,
            docs,
            scanner);
    }
}
