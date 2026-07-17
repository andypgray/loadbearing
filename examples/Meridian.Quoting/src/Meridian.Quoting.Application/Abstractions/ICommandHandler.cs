namespace Meridian.Quoting.Application.Abstractions;

/// <summary>
///     Handles a command of type <typeparamref name="TCommand" />. Carries the
///     <see cref="IHandler" /> marker, so implementations are named <c>*Handler</c>; each one
///     is expected to run under a unit of work and so carries <see cref="TransactionalAttribute" />.
/// </summary>
/// <typeparam name="TCommand">The command this handler accepts.</typeparam>
public interface ICommandHandler<in TCommand> : IHandler
    where TCommand : ICommand
{
    /// <summary>Executes the command.</summary>
    Task HandleAsync(TCommand command);
}