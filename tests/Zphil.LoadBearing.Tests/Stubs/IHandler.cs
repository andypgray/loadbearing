namespace Zphil.LoadBearing.Tests.Stubs;

/// <summary>Open generic interface for the handler naming rule — renders as <c>IHandler&lt;T&gt;</c> (GRAMMAR §5.2).</summary>
/// <typeparam name="T">The handled message type.</typeparam>
public interface IHandler<T>;