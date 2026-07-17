namespace Meridian.Quoting.Application.Abstractions;

/// <summary>
///     Marker for a command: a message that asks the system to change state. Commands are
///     dispatched through <see cref="ICommandBus" />; they are not handlers themselves.
/// </summary>
public interface ICommand
{
}