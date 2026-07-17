using System.Reflection;
using Meridian.Quoting.Application.Abstractions;

namespace Meridian.Quoting.Application.Handlers;

/// <summary>
///     Resolves the handler for a command by its runtime type and invokes it. When the resolved
///     handler carries <see cref="TransactionalAttribute" />, the call is wrapped in
///     <see cref="IUnitOfWork.ExecuteAsync" /> so its writes commit together or roll back together.
/// </summary>
public sealed class CommandBus(IServiceProvider services) : ICommandBus
{
    public async Task SendAsync(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        Type commandType = command.GetType();
        Type handlerType = typeof(ICommandHandler<>).MakeGenericType(commandType);
        object? handler = services.GetService(handlerType);
        if (handler is null) throw new InvalidOperationException($"No command handler is registered for {commandType.Name}.");

        MethodInfo handleMethod = handlerType.GetMethod(nameof(ICommandHandler<ICommand>.HandleAsync))!;

        Task Invoke()
        {
            return (Task)handleMethod.Invoke(handler, [command])!;
        }

        bool isTransactional = handler.GetType().IsDefined(typeof(TransactionalAttribute), false);
        if (!isTransactional)
        {
            await Invoke();
            return;
        }

        if (services.GetService(typeof(IUnitOfWork)) is not IUnitOfWork unitOfWork) throw new InvalidOperationException($"Handler {handler.GetType().Name} is transactional but no IUnitOfWork is registered.");

        await unitOfWork.ExecuteAsync(Invoke);
    }
}