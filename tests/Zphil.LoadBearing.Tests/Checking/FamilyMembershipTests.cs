using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Extraction;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     What a family names when something evaluates it as a plain selection (GRAMMAR §5.1): the union
///     of its cells, in either position, for either form, with the family's own adjectives applied after.
///     The subject path never asks this — it collects the cells itself, because it has emptiness and the
///     partition to report on the way — so the evaluator's own arm is pinned here as the contract every
///     other reader of a family is promised.
/// </summary>
public sealed class FamilyMembershipTests
{
    // Two projects, three types: Sales declares two, Web declares one that references Sales, so a
    // family over the two projects and a union of the two project nouns name the same three types at the
    // same declarers, and a Sales.Reports exclusion has exactly one type to drop.
    private const string SalesFile = """
                                     namespace Sales { public class Order {} }
                                     namespace Sales.Reports { public class Ledger {} }
                                     """;

    private const string WebFile = """
                                   namespace Web { public class Controller { public Sales.Order O; } }
                                   """;

    private static readonly CodebaseModel TwoProjects = Extract();

    private static readonly string[] AllThree = ["Sales.Order", "Sales.Reports.Ledger", "Web.Controller"];

    // The position is a bool at the theory's surface because SelectionPosition is internal and a public
    // test method cannot take it; both positions are exercised, which is the point of the theory.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFamilyOfLayers_EvaluatesToTheUnionOfItsCells(bool asSubject)
    {
        var arch = new Arch();
        Selection family = arch.Each(arch.Layer("Sales", arch.Project("Sales")), arch.Layer("Web", arch.Project("Web")));
        Selection union = arch.AnyOf(arch.Project("Sales"), arch.Project("Web"));
        SelectionPosition position = PositionOf(asSubject);

        IReadOnlyList<string> viaFamily = Names(family, position);
        IReadOnlyList<string> viaUnion = Names(union, position);

        viaFamily.ShouldBe(AllThree);
        viaFamily.ShouldBe(viaUnion);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFamilyOfProjects_EvaluatesToWhatItsProjectsDeclare(bool asSubject)
    {
        var arch = new Arch();
        Selection family = arch.Each(arch.Projects.Named("Sales", "Web"));
        Selection union = arch.AnyOf(arch.Project("Sales"), arch.Project("Web"));
        SelectionPosition position = PositionOf(asSubject);

        IReadOnlyList<string> viaFamily = Names(family, position);
        IReadOnlyList<string> viaUnion = Names(union, position);

        viaFamily.ShouldBe(AllThree);
        viaFamily.ShouldBe(viaUnion);
    }

    private static SelectionPosition PositionOf(bool asSubject)
    {
        return asSubject ? SelectionPosition.Subject : SelectionPosition.Target;
    }

    [Fact]
    public void AFamilysOwnAdjectives_NarrowTheUnionOfItsCells()
    {
        // An Except on the family narrows every cell's members (GRAMMAR §5.1): the union minus the cone,
        // whichever cell the excluded type sat in.
        var arch = new Arch();
        Selection narrowed = arch.Each(arch.Projects.Named("Sales", "Web")).Except(arch.Namespace("Sales.Reports.*"));

        IReadOnlyList<string> names = Names(narrowed, SelectionPosition.Subject);

        names.ShouldBe(["Sales.Order", "Web.Controller"]);
    }

    private static IReadOnlyList<string> Names(Selection selection, SelectionPosition position)
    {
        var evaluator = new SelectionEvaluator(TwoProjects);
        HashSet<TypeNode> members = evaluator.Evaluate(selection, position);
        return members
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static CodebaseModel Extract()
    {
        CompilationInput sales = CompilationFactory.Compile("Sales", ("Sales.cs", SalesFile));
        CompilationInput web = CompilationFactory.CompileReferencing(
            "Web", sales.Compilation, "Sales", ("Web.cs", WebFile));

        return CodebaseExtractor.ExtractFromCompilations([sales, web]);
    }
}
