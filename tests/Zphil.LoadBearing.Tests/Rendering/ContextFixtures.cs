using Zphil.LoadBearing.Tests.Checking;

namespace Zphil.LoadBearing.Tests.Rendering;

/// <summary>
///     Spec fixtures the context-rendering suites share. A scene two renderer suites are pinned
///     against is held once: the pins only mean what they say while the input is literally the same.
/// </summary>
internal static class ContextFixtures
{
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
}
