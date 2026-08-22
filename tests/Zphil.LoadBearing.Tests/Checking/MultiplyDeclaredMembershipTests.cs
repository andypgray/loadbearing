using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Extraction;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The one bed in the fast-path checker tests where a single source file compiles into more than one
///     project (GRAMMAR §4.1), shared by this class, <see cref="MultiplyDeclaredAttributionTests" /> and
///     <see cref="MustResideInProjectVerbTests" /> — membership, edge attribution, and the residence verb
///     read the same three declarers rather than each staging a multi-project extraction of its own.
/// </summary>
/// <remarks>
///     <para>
///         Four projects, handed to extraction in this order, so <c>Core</c> is the first declarer and its
///         facts are the ones the shared nodes carry:
///     </para>
///     <list type="bullet">
///         <item>
///             <c>Core</c> — the linked file, plus <c>Core.CoreOnly</c> and <c>Core.User</c>, whose field
///             is the winner's own reference into its own copy.
///         </item>
///         <item>
///             <c>Tool</c> — the same linked file, plus <c>Tool.Command</c>, which both references and
///             constructs the copy Tool compiles itself. It references no other project.
///         </item>
///         <item>
///             <c>Stub</c> — the linked file and nothing else, so <c>arch.Project("Stub")</c> names none
///             but shared types.
///         </item>
///         <item>
///             <c>Client</c> — a real reference to Core's compilation, and <c>Client.Consumer</c>'s field
///             is the genuine cross-project reference into the winner's copy.
///         </item>
///     </list>
///     <para>
///         So <c>Shared.Widget</c> and <c>Shared.WidgetPart</c> are each one node declared by Core, Stub and
///         Tool, and the four reference edges around them span every case the attribution rule parts:
///         <c>Shared.Widget → Shared.WidgetPart</c> (both ends multiply declared),
///         <c>
///             Core.User →
///             Shared.Widget
///         </c>
///         and <c>Tool.Command → Shared.Widget</c> (a declarer reaching its own compiled-in
///         copy, from the winner and from a loser), and <c>Client.Consumer → Shared.Widget</c> (a project
///         that declares nothing shared reaching the winner's declaration).
///     </para>
/// </remarks>
internal static class MultiplyDeclaredCodebase
{
    // The file all three declaring projects compile. Self-contained by necessity: Stub and Tool reference
    // nothing at all, so it may name only types it declares itself. Widget's field is the outward edge from
    // a multiply-declared type, and Run() is what a member selection anchored on a declarer reaches.
    private const string LinkedFile = """
                                      namespace Shared
                                      {
                                          public class Widget
                                          {
                                              public WidgetPart Part;
                                              public void Run() {}
                                          }
                                          public class WidgetPart {}
                                      }
                                      """;

    // The winner's own types: one it alone declares, and one whose field reaches the copy it compiles itself.
    private const string CoreOwn = """
                                   namespace Core
                                   {
                                       public class CoreOnly {}
                                       public class User { public Shared.Widget W; }
                                   }
                                   """;

    // The loser's own type. The field is a reference edge and the method body a construction edge, both onto
    // the copy Tool compiles itself — which is what makes the two verbs separable over one endpoint pair.
    private const string ToolOwn = """
                                   namespace Tool
                                   {
                                       public class Command
                                       {
                                           public Shared.Widget W;
                                           public Shared.Widget Make() => new Shared.Widget();
                                       }
                                   }
                                   """;

    // The control: Client declares no copy of the shared file, so its field binds Widget out of Core's
    // assembly — a reference across a project boundary rather than into a copy of its own.
    private const string ClientOwn = """
                                     namespace Client
                                     {
                                         public class Consumer { public Shared.Widget W; }
                                     }
                                     """;

    /// <summary>
    ///     The extracted bed, built once for both classes — extraction is the expensive half of a
    ///     fast-path row, and sibling classes only mean the same thing while the input is literally shared.
    /// </summary>
    internal static CodebaseModel Model { get; } = Extract();

    private static CodebaseModel Extract()
    {
        // The linked file is handed to each declaring project under the one path a <Compile Include> link
        // gives it, which is the shape the merge sees in a real workspace.
        CompilationInput core = CompilationFactory.Compile(
            "Core", ("Shared/Widget.cs", LinkedFile), ("Core.cs", CoreOwn));
        CompilationInput tool = CompilationFactory.Compile(
            "Tool", ("Shared/Widget.cs", LinkedFile), ("Command.cs", ToolOwn));
        CompilationInput stub = CompilationFactory.Compile("Stub", ("Shared/Widget.cs", LinkedFile));
        CompilationInput client = CompilationFactory.CompileReferencing(
            "Client", core.Compilation, "Core", ("Consumer.cs", ClientOwn));

        // Input order decides the winner, so Core leads.
        return CodebaseExtractor.ExtractFromCompilations([core, tool, stub, client]);
    }
}

/// <summary>
///     N-way membership over a type several projects declare (GRAMMAR §4.1): one source file compiled into
///     three projects is one node, and <c>arch.Project</c> naming any declarer reaches it — as a subject, as
///     a target operand, under a member projection, and inside a union. <c>Except</c> subtracts by node
///     identity across declarers, and the two gates that read a selection's arity — the per-operand
///     empty-subject failure and the inert-target warning — see the wider set that membership produces.
/// </summary>
/// <remarks>
///     The bed is <see cref="MultiplyDeclaredCodebase" />, which the class-level remarks there describe in
///     full; <see cref="MultiplyDeclaredAttributionTests" /> carries the edge-attribution half over the same
///     model.
/// </remarks>
public sealed class MultiplyDeclaredMembershipTests
{
    [Fact]
    public void Selects_ProjectNounNamingALoserDeclarer_IncludesTheSharedType()
    {
        // Tool declares its own copy of the linked file, so the project noun over Tool names the shared
        // types beside Tool's own — even though the nodes' facts follow Core.
        Checker.Selects(MultiplyDeclaredCodebase.Model, arch => arch.Project("Tool"))
            .ShouldBe(["Shared.Widget", "Shared.WidgetPart", "Tool.Command"]);
    }

    [Fact]
    public void Selects_ProjectNounNamingTheWinner_StillIncludesTheSharedType()
    {
        // The winner's side of the same claim: membership is N-way rather than moved, so the declarer whose
        // facts the nodes carry keeps them too.
        Checker.Selects(MultiplyDeclaredCodebase.Model, arch => arch.Project("Core"))
            .ShouldBe(["Core.CoreOnly", "Core.User", "Shared.Widget", "Shared.WidgetPart"]);
    }

    [Fact]
    public void Selects_ExceptNamingAnotherDeclarer_RemovesTheSharedNodeByIdentity()
    {
        // One node, two selections: Except subtracts the instances Project("Tool") matched, and the shared
        // types are those same instances, so they leave Core's selection with them. Core's own two stay.
        Checker.Selects(MultiplyDeclaredCodebase.Model, arch => arch.Project("Core").Except(arch.Project("Tool")))
            .ShouldBe(["Core.CoreOnly", "Core.User"]);
    }

    [Fact]
    public void MustHaveSuffix_SubjectAnchoredOnALoserDeclarer_RangesOverTheSharedType()
    {
        // A shape verb sees the same subject set the selection does: anchored on Tool, the rule ranges over
        // the copy Tool compiles as well as over Tool's own type, so both shared types answer for the suffix.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("naming/tool-commands")
                    .Enforce(arch.Project("Tool").MustHaveSuffix("Command"))
                    .Because("A tool's types are named for the command they run."))
            .Single()
            .ShouldHaveFailedWithSubjects(["Shared.Widget", "Shared.WidgetPart"]);
    }

    [Fact]
    public void MemberSubject_AnchoredOnALoserDeclarer_ReachesTheSharedTypesMembers()
    {
        // A member projection resolves its members from the type subject, so N-way membership carries
        // through it: Widget.Run is a member of a type Tool compiles, and the rule reaches it.
        RuleResult result = Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("naming/tool-methods")
                    .Enforce(arch.Project("Tool").Methods.WithPrefix("Run").MustHaveSuffix("Async"))
                    .Because("Awaitable methods are discovered by suffix."))
            .Single();

        result.MemberShapeSubjects()
            .ShouldBe(["M:Shared.Widget.Run"]);
    }

    [Fact]
    public void UnionSubjectOperand_ProjectDeclaringOnlyALinkedCopy_NoLongerFailsEmpty()
    {
        // Stub declares the linked file and nothing else, so every type it names is one another project
        // declares too. That is still a match, and §9's per-operand gate has nothing to report.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("naming/union")
                    .Enforce(arch.AnyOf(arch.Project("Core"), arch.Project("Stub")).MustHaveNameMatching("*"))
                    .Because("Every type answers for its name."))
            .Single()
            .ShouldHavePassedClean();

        // The same union with an operand naming no project at all, so the green above is the gate agreeing
        // rather than the gate absent.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("naming/union")
                    .Enforce(arch.AnyOf(arch.Project("Core"), arch.Project("Nope")).MustHaveNameMatching("*"))
                    .Because("Every type answers for its name."))
            .Single()
            .ShouldHaveFailedWithDetail(
                ViolationKind.EmptySubject,
                "The subject selection operand \"types in project `Nope`\" matched no solution-declared types.");
    }

    [Fact]
    public void MustNotReference_PatternTargetNamingALoserDeclarer_NoLongerWarnsInert()
    {
        // Project("Stub") matches the two types Stub compiles from the linked file, so the target selection
        // is live and the rule is not inert. It still forbids nothing here: Consumer's reference is
        // attributed to the winner, not to Stub — which is what makes the clean pass a statement about the
        // warning rather than about an empty target.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/client-not-stub")
                    .Enforce(arch.Project("Client").MustNotReference(arch.Project("Stub")))
                    .Because("A client depends on the products it was built against."))
            .Single()
            .ShouldHavePassedClean();
    }
}
