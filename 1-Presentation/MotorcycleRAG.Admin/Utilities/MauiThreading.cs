namespace MotorcycleRAG.Admin.Utilities;

internal static class MauiThreading
{
    internal static Task RunOnMainThreadAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (MainThread.IsMainThread)
        {
            action();
            return Task.CompletedTask;
        }

        return MainThread.InvokeOnMainThreadAsync(action);
    }

    internal static Task RunOnMainThreadAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (MainThread.IsMainThread)
        {
            return action();
        }

        return MainThread.InvokeOnMainThreadAsync(action);
    }

    internal static Task<T> RunOnMainThreadAsync<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (MainThread.IsMainThread)
        {
            return Task.FromResult(action());
        }

        return MainThread.InvokeOnMainThreadAsync(action);
    }

    internal static Task<T> RunOnMainThreadAsync<T>(Func<Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (MainThread.IsMainThread)
        {
            return action();
        }

        return MainThread.InvokeOnMainThreadAsync(action);
    }

    internal static Task RunOffMainThreadAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await action().ConfigureAwait(false);
        }, cancellationToken);
    }

    internal static Task RunOffMainThreadAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
        }, cancellationToken);
    }

    internal static Task<T> RunOffMainThreadAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await action().ConfigureAwait(false);
        }, cancellationToken);
    }

    internal static Task<T> RunOffMainThreadAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return action();
        }, cancellationToken);
    }
}
