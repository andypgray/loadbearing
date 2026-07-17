using Meridian.Quoting.Application.Abstractions;

namespace Meridian.Quoting.Infrastructure.Persistence;

/// <summary>
///     Runs work against <see cref="InMemoryDatabase" /> atomically: it captures a snapshot, runs
///     the work, and on failure restores the snapshot before rethrowing, so a batch of writes
///     never lands half-applied.
/// </summary>
public sealed class InMemoryUnitOfWork(InMemoryDatabase database) : IUnitOfWork
{
    public async Task ExecuteAsync(Func<Task> work)
    {
        InMemoryDatabase.Snapshot snapshot = database.Capture();
        try
        {
            await work();
        }
        catch
        {
            database.Restore(snapshot);
            throw;
        }
    }
}