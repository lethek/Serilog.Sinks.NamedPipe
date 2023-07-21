namespace Serilog.Sinks.NamedPipe.Tests;

internal static class CancellationTokenExtensions
{
    public static Task<bool> ToTask(this CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) {
            return Task.FromResult(true);
        }

        var tcs = new TaskCompletionSource<bool>();
        cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
        return tcs.Task;
    }
}
