namespace PolyglotApp.Core
{
    public interface IWidget
    {
        string Name { get; }
    }

    public sealed class Widget : IWidget
    {
        public string Name => "widget";
    }
}
