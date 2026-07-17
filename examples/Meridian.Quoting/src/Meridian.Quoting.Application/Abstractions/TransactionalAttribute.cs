namespace Meridian.Quoting.Application.Abstractions;

/// <summary>
///     Marks a command handler whose work must run inside a unit of work. The
///     <see cref="ICommandBus" /> reads this attribute off the resolved handler and, when present,
///     wraps the handler call in <see cref="IUnitOfWork.ExecuteAsync" />.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TransactionalAttribute : Attribute
{
}