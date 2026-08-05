using Shouldly;
using Xunit;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The negative hierarchy verbs over the real MyApp fixture (GRAMMAR §5.3), reusing the shared
///     <see cref="WorkspaceFixture.Model" /> (no extra MSBuild load). The reds already live in the committed
///     fixture, so each verb pins its known violator (red) and a scoped-out non-violator (green). The
///     interface and attribute anchors are fully-qualified strings (GRAMMAR §5.2), which is what lets this
///     class name MyApp types without a reflectable stub declaring them: correspondence to the extracted
///     model is by full name either way, and a string names the correspondence instead of impersonating it.
///     <c>System.Exception</c> is the one external (BCL) base anchor, and the one that still wants a
///     <c>typeof</c> — the spec side of this test compiles against the BCL already.
/// </summary>
public sealed class CheckerFixtureHierarchyNegativeTests(WorkspaceFixture fixture)
{
    [Fact]
    public void MustNotImplement_OpenGeneric_RedsInvoiceCreatedHandler_PassesHomeController()
    {
        // InvoiceCreatedHandler : IHandler<InvoiceCreated> — the open-generic construction match reds it.
        Checker.Run(fixture.Model, arch => arch.Rule("hierarchy/no-handlers")
                .Enforce(arch.Types.WithPrefix("InvoiceCreatedHandler").MustNotImplement("MyApp.Web.IHandler<T>"))
                .Because("b"))
            .Single().ShapeSubjects().ShouldBe(["MyApp.Web.InvoiceCreatedHandler"]);

        // HomeController does not implement IHandler — the ban silently passes.
        Checker.Run(fixture.Model, arch => arch.Rule("hierarchy/no-handlers")
                .Enforce(arch.Types.WithPrefix("HomeController").MustNotImplement("MyApp.Web.IHandler<T>"))
                .Because("b"))
            .Single().ShouldHavePassed();
    }

    [Fact]
    public void MustNotDeriveFrom_ExternalBaseException_RedsOrderRuleViolation_PassesOrderApproval()
    {
        // OrderRuleViolation : System.Exception — the direct external base is recorded, so the ban reds it.
        Checker.Run(fixture.Model, arch => arch.Rule("hierarchy/no-bcl-exceptions")
                .Enforce(arch.Types.WithPrefix("OrderRuleViolation").MustNotDeriveFrom(typeof(Exception)))
                .Because("b"))
            .Single().ShapeSubjects().ShouldBe(["MyApp.Domain.OrderRuleViolation"]);

        // OrderApproval derives from no BCL exception — the ban silently passes.
        Checker.Run(fixture.Model, arch => arch.Rule("hierarchy/no-bcl-exceptions")
                .Enforce(arch.Types.WithPrefix("OrderApproval").MustNotDeriveFrom(typeof(Exception)))
                .Because("b"))
            .Single().ShouldHavePassed();
    }

    [Fact]
    public void MustNotBeAttributedWith_WebRoute_RedsHomeController_PassesInvoiceCreatedHandler()
    {
        // HomeController carries [WebRoute("/home")] — the declared attribute reds it.
        Checker.Run(fixture.Model, arch => arch.Rule("hierarchy/no-webroute")
                .Enforce(arch.Types.WithPrefix("HomeController").MustNotBeAttributedWith("MyApp.Web.WebRouteAttribute"))
                .Because("b"))
            .Single().ShapeSubjects().ShouldBe(["MyApp.Web.HomeController"]);

        // InvoiceCreatedHandler carries no attribute — the ban silently passes.
        Checker.Run(fixture.Model, arch => arch.Rule("hierarchy/no-webroute")
                .Enforce(arch.Types.WithPrefix("InvoiceCreatedHandler").MustNotBeAttributedWith("MyApp.Web.WebRouteAttribute"))
                .Because("b"))
            .Single().ShouldHavePassed();
    }
}
