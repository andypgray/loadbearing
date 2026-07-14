using Shouldly;
using Xunit;
using Zphil.LoadBearing.Checking;
using Zphil.LoadBearing.Tests.TestSupport;

namespace Zphil.LoadBearing.Tests.Checking;

/// <summary>
///     The checker over the real MyApp fixture, reusing the shared <see cref="WorkspaceFixture.Model" />
///     (no extra MSBuild load): the acceptance rule fires with a locatable site, a clean rule passes,
///     and the I-prefix naming rule holds.
/// </summary>
public sealed class CheckerFixtureIntegrationTests(WorkspaceFixture fixture)
{
    [Fact]
    public void DomainMustNotReferenceWeb_FiresOnOrderServiceWithLocation()
    {
        RuleResult result = Checker.Run(fixture.Model, arch =>
                arch.Rule("layering/domain-independent")
                    .Enforce(arch.Layer("Domain", "MyApp.Domain.*").MustNotReference(arch.Layer("Web", "MyApp.Web.*")))
                    .Because("Domain is UI-agnostic; transaction boundaries live in services.")
                    .Fix("Define an abstraction in Domain and implement it in Web."))
            .Single();

        result.Status.ShouldBe(RuleStatus.Failed);
        result.ReferencePairs().ShouldContain("MyApp.Domain.OrderService -> MyApp.Web.HomeController");
        result.ReferencePairs().ShouldContain("MyApp.Domain.OrderService -> MyApp.Web.WebTextExtensions");

        Violation homeController = result.Violations.Single(v =>
            v.Source!.FullName == "MyApp.Domain.OrderService" && v.Target!.FullName == "MyApp.Web.HomeController");
        homeController.Sites.Select(s => fixture.RelativePath(s)).ShouldContain("MyApp.Domain/OrderService.cs");
        homeController.Sites.Select(s => s.Line).ShouldContain(9);
    }

    [Fact]
    public void BillingMustNotReferenceWeb_Passes()
    {
        Checker.Run(fixture.Model, arch =>
                arch.Rule("layering/billing-independent")
                    .Enforce(arch.Namespace("MyApp.Legacy.Billing.*").MustNotReference(arch.Namespace("MyApp.Web.*")))
                    .Because("Billing must not reach up into the web layer."))
            .Single().Status.ShouldBe(RuleStatus.Passed);
    }

    [Fact]
    public void InterfaceIPrefixNaming_Holds()
    {
        Checker.Run(fixture.Model, arch =>
                arch.Rule("naming/interfaces")
                    .Enforce(arch.Types.OfKind(TypeKind.Interface).InNamespace("MyApp.*").MustHavePrefix("I"))
                    .Because("House naming convention; agents grep by I-prefix."))
            .Single().Status.ShouldBe(RuleStatus.Passed);
    }
}