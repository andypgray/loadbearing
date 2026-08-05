namespace BrokenApp.Web
{
    // Reaches the absent project on all three ungated external-mint feeders at once — base type, interface,
    // and attribute class — so extraction meets an unresolved symbol on every path that used to crash it.
    [BrokenApp.Contracts.Tracked]
    public class WidgetController : BrokenApp.Contracts.ControllerBase, BrokenApp.Contracts.IWidgetEndpoint
    {
        public string Describe(BrokenApp.Core.Widget widget)
        {
            return widget.Name;
        }
    }
}
