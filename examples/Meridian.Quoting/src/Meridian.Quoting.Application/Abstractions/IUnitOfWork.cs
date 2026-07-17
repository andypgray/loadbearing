namespace Meridian.Quoting.Application.Abstractions;

/// <summary>
///     Runs a unit of work so that all of its store mutations commit together or none do. A
///     handler that writes more than once relies on this to avoid leaving a partial write behind
///     when a later step fails.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Runs <paramref name="work" />, discarding every mutation it made if it throws.</summary>
    Task ExecuteAsync(Func<Task> work);
}