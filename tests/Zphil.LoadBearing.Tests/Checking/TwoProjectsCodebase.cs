using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Roslyn;
using Zphil.LoadBearing.Roslyn.Extraction;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     Two projects, one reference between them — Sales declaring an order and a ledger, Web holding a
///     controller that reads the order — extracted once for every test that ranges a layer, a family or a
///     definition over the pair.
/// </summary>
internal static class TwoProjectsCodebase
{
    private const string SalesFile = """
                                     namespace Sales { public class Order {} }
                                     namespace Sales.Reports { public class Ledger {} }
                                     """;

    private const string WebFile = """
                                   namespace Web { public class Controller { public Sales.Order O; } }
                                   """;

    internal static CodebaseModel Model { get; } = Extract();

    private static CodebaseModel Extract()
    {
        CompilationInput sales = CompilationFactory.Compile("Sales", ("Sales.cs", SalesFile));
        CompilationInput web = CompilationFactory.CompileReferencing(
            "Web", sales.Compilation, "Sales", ("Web.cs", WebFile));

        return CodebaseExtractor.ExtractFromCompilations([sales, web]);
    }
}
