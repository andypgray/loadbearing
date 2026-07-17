namespace Meridian.Quoting.Application.Abstractions;

/// <summary>
///     Dispatches a command to its registered <see cref="ICommandHandler{TCommand}" />. This is a
///     dispatcher, not a handler, so it does not carry the <see cref="IHandler" /> marker.
/// </summary>
public interface ICommandBus
{
    /// <summary>Routes the command to its handler, running it under a unit of work when the handler is transactional.</summary>
    Task SendAsync(ICommand command);
}