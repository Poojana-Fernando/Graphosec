using ResumableCopy.Application.Abstractions;

namespace ResumableCopy.Application.Services;

public sealed class ThreadPoolBackgroundExecutor : IBackgroundExecutor
{
    public Task RunAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return Task.Run(() => work(cancellationToken), cancellationToken);
    }

    public Task<TResult> RunAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return Task.Run(() => work(cancellationToken), cancellationToken);
    }
}
