namespace LayoutApp.Core
{
    // The spec's subject. A shape rule over the interfaces here needs no target selection, so the rule is
    // never inert — an inert rule would warn, and a bed whose whole assertion is "the spec resolved and ran"
    // must not have to explain a warning line.
    public interface ILedger
    {
        decimal Balance { get; }

        void Post(decimal amount);
    }
}
