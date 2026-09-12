using Zphil.LoadBearing.Codebase;
using Zphil.LoadBearing.Tests.Checking;
using Zphil.LoadBearing.Tests.Extraction;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     Spec fixtures the context-rendering suites share. A scene two renderer suites are pinned
///     against is held once: the pins only mean what they say while the input is literally the same.
/// </summary>
internal static class ContextFixtures
{
    /// <summary>
    ///     The one-file Web codebase the scenes below are placed against: a single controller under
    ///     <c>src/MyApp.Web</c>, which is the directory both suites expect a Web card to land in.
    /// </summary>
    internal static readonly CodebaseModel WebCodebase = CompilationFactory.Extract("MyApp.Web",
        ("src/MyApp.Web/HomeController.cs", "namespace MyApp.Web; public class HomeController {}"));

    /// <summary>
    ///     A Web layer with one bare-subject Enforce rule anchored on it — saying what it is for when
    ///     given a purpose, which is the sentence the card lede carries.
    /// </summary>
    internal static IArchitectureSpec WebLayer(string? purpose = null)
    {
        return new InlineSpec(arch =>
        {
            Layer web = arch.Layer("Web", "MyApp.Web.*");
            if (purpose is not null) web.Purpose(purpose);
            arch.Rule("layering/web-not-billing")
                .Enforce(web.MustNotReference(arch.Namespace("MyApp.Legacy.Billing.*")))
                .Because("Web reaches billing only through the facade.");
        });
    }

    /// <summary>
    ///     The mirror of <see cref="WebLayer" />: a Billing layer with one bare-subject Enforce rule
    ///     anchored on it. Declared as an <see cref="Arch" /> action rather than a spec so a caller can
    ///     compose a scope statement beside it.
    /// </summary>
    internal static void BillingLayer(Arch arch)
    {
        Layer billing = arch.Layer("Billing", "MyApp.Legacy.Billing.*");
        arch.Rule("layering/billing-not-web")
            .Enforce(billing.MustNotReference(arch.Namespace("MyApp.Web.*")))
            .Because("Billing is downstream of the web layer.");
    }
}
