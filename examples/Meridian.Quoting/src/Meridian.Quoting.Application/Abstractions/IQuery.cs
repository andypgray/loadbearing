namespace Meridian.Quoting.Application.Abstractions;

/// <summary>
///     Marker for a query: a message that reads state and yields <typeparamref name="TResult" />
///     without changing anything.
/// </summary>
/// <typeparam name="TResult">The shape the query returns.</typeparam>
public interface IQuery<TResult>
{
}