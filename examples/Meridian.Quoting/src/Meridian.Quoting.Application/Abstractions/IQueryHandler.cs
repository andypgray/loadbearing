namespace Meridian.Quoting.Application.Abstractions;

/// <summary>
///     Handles a query of type <typeparamref name="TQuery" /> and returns
///     <typeparamref name="TResult" />. Carries the <see cref="IHandler" /> marker, so
///     implementations are named <c>*Handler</c>.
/// </summary>
/// <typeparam name="TQuery">The query this handler accepts.</typeparam>
/// <typeparam name="TResult">The shape the query returns.</typeparam>
public interface IQueryHandler<in TQuery, TResult> : IHandler
    where TQuery : IQuery<TResult>
{
    /// <summary>Answers the query.</summary>
    Task<TResult> HandleAsync(TQuery query);
}