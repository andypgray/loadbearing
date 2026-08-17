using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.DocHygiene;

/// <summary>
///     The gate that keeps every <c>actions/checkout</c> step across the workflows opted out of
///     credential persistence.
/// </summary>
/// <remarks>
///     <para>
///         <b>What goes wrong without it.</b> <c>actions/checkout</c> writes the job's
///         <c>GITHUB_TOKEN</c> into <c>.git/config</c> and leaves it there for the rest of the run, so
///         every later step — including whatever a third-party action executes — can read a credential
///         it was never handed. <c>persist-credentials: false</c> is the opt-out, and it has to be
///         repeated on every checkout because it is a per-step input rather than a workflow setting.
///     </para>
///     <para>
///         <b>Why uniformity needs a gate and not review.</b> Nine of the ten checkouts carried the
///         input and one did not, which is the shape this class exists for: nothing in the YAML
///         complains, both spellings run green, and the odd one out is invisible until someone reads
///         all ten side by side. Scorecard reads the pattern across the repository, so the cost of the
///         gap is not confined to the workflow that has it.
///     </para>
///     <para>
///         <b>Moving the pin.</b> A workflow that genuinely needs to push — one that commits, tags, or
///         opens a pull request — needs the credential, and this test will red for it. That is the
///         intended path: widen it deliberately as part of that change, naming the step and why it is
///         exempt, rather than dropping the input quietly.
///     </para>
/// </remarks>
public sealed class CheckoutCredentialPersistenceTests
{
    private const string WorkflowDirectory = ".github/workflows/";

    [Fact]
    public void CheckoutSteps_AcrossTheWorkflows_DoNotPersistCredentials()
    {
        // Arrange
        IReadOnlyList<CheckoutStep> steps = ReadCheckoutSteps();

        // Act
        List<string> persisting = steps
            .Where(static step => !step.OptsOut)
            .Select(static step => $"  {step.Workflow}:{step.Line}")
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert
        persisting.ShouldReportNothing(
            "actions/checkout step(s) omit 'persist-credentials: false', leaving the job's GITHUB_TOKEN "
            + "in .git/config for every step that follows");
    }

    [Fact]
    public void CheckoutSteps_AreReadable()
    {
        // Act
        IReadOnlyList<CheckoutStep> steps = ReadCheckoutSteps();

        // Assert: the arm above passes over a workflow it cannot read, so the steps are held non-empty
        // rather than trusted. A rewrite into a form this scan cannot follow fails here.
        steps.ShouldNotBeEmpty($"No actions/checkout steps were found under {WorkflowDirectory}.");
    }

    // Every use of the action across the tracked workflows, tagged with the file and line it sits on.
    private static IReadOnlyList<CheckoutStep> ReadCheckoutSteps()
    {
        return TrackedFiles.All
            .Where(static path => path.StartsWith(WorkflowDirectory, StringComparison.Ordinal))
            .SelectMany(StepsIn)
            .ToArray();
    }

    private static IEnumerable<CheckoutStep> StepsIn(string workflow)
    {
        string[] lines = RepoRoot.ReadLines(workflow);

        return Enumerable.Range(0, lines.Length)
            .Where(index => IsCheckoutUse(lines[index]))
            .Select(index => ToStep(workflow, lines, index))
            .ToList();
    }

    private static bool IsCheckoutUse(string line)
    {
        return line.Contains("uses:", StringComparison.Ordinal)
               && line.Contains("actions/checkout@", StringComparison.Ordinal);
    }

    private static CheckoutStep ToStep(string workflow, string[] lines, int index)
    {
        int indent = IndentOf(lines[index]);
        IEnumerable<string> inputs = lines
            .Skip(index + 1)
            .TakeWhile(line => ContinuesStep(line, indent));
        bool optsOut = inputs.Any(static line =>
            line.Contains("persist-credentials:", StringComparison.Ordinal)
            && line.Contains("false", StringComparison.Ordinal));

        return new CheckoutStep(workflow, index + 1, optsOut);
    }

    // The step's inputs run until the next list item, or until a line that dedents out of the block
    // entirely — the next job's key. A blank line is neither, so a step whose inputs follow one is
    // still read as one step.
    private static bool ContinuesStep(string line, int indent)
    {
        if (line.Trim().Length == 0) return true;

        if (line.TrimStart().StartsWith("- ", StringComparison.Ordinal)) return false;

        return IndentOf(line) >= indent;
    }

    private static int IndentOf(string line)
    {
        return line.Length - line.TrimStart().Length;
    }

    private sealed record CheckoutStep(string Workflow, int Line, bool OptsOut);
}
