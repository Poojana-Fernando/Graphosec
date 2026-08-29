namespace ResumableCopy.Application.Abstractions;

public interface IBackgroundExecutor
{
    Task RunAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default);

    Task<TResult> RunAsync<TResult>(Func<CancellationToken, Task<TResult>> work, CancellationToken cancellationToken = default);
}
