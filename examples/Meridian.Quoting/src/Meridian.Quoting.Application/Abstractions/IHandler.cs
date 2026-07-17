namespace Meridian.Quoting.Application.Abstractions;

/// <summary>
///     Marker for a message handler. It sits on the handler interfaces only
///     (<see cref="ICommandHandler{TCommand}" />, <see cref="IQueryHandler{TQuery,TResult}" />),
///     so every type that carries it is a handler and is named accordingly.
/// </summary>
public interface IHandler
{
}