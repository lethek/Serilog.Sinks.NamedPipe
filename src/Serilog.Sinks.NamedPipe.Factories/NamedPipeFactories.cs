using System.IO.Pipes;

namespace Serilog.Sinks.NamedPipe;


/// <summary>
/// Provides static methods for creating named-pipe stream factories (<see cref="PipeStreamFactory"/>).
/// </summary>
public static class NamedPipeFactories
{
    /// <summary>
    /// Allows easily creating a custom <see cref="PipeStreamFactory"/> for configuring the sink. It consists of two separate steps:
    /// <list type="number">
    /// <item><description>Create a <see cref="PipeStream"/> instance (e.g. <see cref="NamedPipeClientStream"/>, <see cref="NamedPipeServerStream"/>, <see cref="AnonymousPipeClientStream"/>, <see cref="AnonymousPipeServerStream"/>).</description></item>
    /// <item><description>Connect the <see cref="PipeStream"/> instance.</description></item>
    /// </list>
    /// The resulting factory will automatically take care of disposing the <see cref="PipeStream"/> instance internally if the connection fails.
    /// </summary>
    /// <typeparam name="T">The type of the stream returned by the <paramref name="getStream"/> delegate. It must implement the <see cref="PipeStream"/> class.</typeparam>
    /// <param name="getStream">A delegate function which returns a new instance of a <see cref="PipeStream"/> implementation.</param>
    /// <param name="connect">An async delegate which performs the task of waiting for the created <see cref="PipeStream"/> to connect. A <see cref="CancellationToken"/> is
    /// passed to the delegate so that the wait may be cancelled. If cancellation is requested, the delegate MUST allow the <see cref="OperationCanceledException"/>
    /// to be thrown and remain unhandled by the delegate.</param>
    /// <returns>A factory method which, when called, creates and connects the named-pipe.</returns>
    public static PipeStreamFactory CreateFactory<T>(Func<T> getStream, Func<T, CancellationToken, Task> connect)
        where T : PipeStream
        => cancellationToken
            => TryOrDispose<T, PipeStream>(
                getStream(),
                x => connect(x, cancellationToken)
            );


    /// <summary>
    /// Allows easily creating a custom <see cref="PipeStreamFactory"/> for configuring the sink. It consists of two separate steps:
    /// <list type="number">
    /// <item><description>Create a <see cref="PipeStream"/> instance (e.g. <see cref="NamedPipeClientStream"/>, <see cref="NamedPipeServerStream"/>, <see cref="AnonymousPipeClientStream"/>, <see cref="AnonymousPipeServerStream"/>).</description></item>
    /// <item><description>Connect the <see cref="PipeStream"/> instance.</description></item>
    /// </list>
    /// The resulting factory will automatically take care of disposing the <see cref="PipeStream"/> instance internally if the connection fails.
    /// </summary>
    /// <typeparam name="T">The type of the stream returned by the <paramref name="getStream"/> delegate. It must implement the <see cref="PipeStream"/> class.</typeparam>
    /// <param name="getStream">An async delegate function which returns a new instance of a <see cref="PipeStream"/> implementation.</param>
    /// <param name="connect">An async delegate which performs the task of waiting for the created <see cref="PipeStream"/> to connect. A <see cref="CancellationToken"/> is
    /// passed to the delegate so that the wait may be cancelled. If cancellation is requested, the delegate MUST allow the <see cref="OperationCanceledException"/>
    /// to be thrown and remain unhandled by the delegate.</param>
    /// <returns>A factory method which, when called, creates and connects the named-pipe.</returns>
    public static PipeStreamFactory CreateFactory<T>(Func<ValueTask<T>> getStream, Func<T, CancellationToken, Task> connect)
        where T : PipeStream
        => async cancellationToken
            => await TryOrDispose<T, PipeStream>(
                await getStream(),
                x => connect(x, cancellationToken)
            );


    /// <summary>
    /// The default <see cref="PipeStreamFactory"/> for creating a <see cref="NamedPipeClientStream"/> instance. The pipe will be asynchronous and assumed to be located on the local machine.
    /// </summary>
    /// <param name="pipeName">The name of the named-pipe.</param>
    /// <param name="direction">The direction of the named-pipe communication from this side. The default is <see cref="E:PipeDirection.Out"/>.</param>
    /// <returns>A factory method which, when called, creates and connects the named-pipe.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pipeName"/> is <see langword="null" />.</exception>
    public static PipeStreamFactory DefaultClientFactory(string pipeName, PipeDirection direction = PipeDirection.Out)
        => String.IsNullOrWhiteSpace(pipeName)
            ? throw new ArgumentNullException(nameof(pipeName))
            : CreateFactory(
                () => new NamedPipeClientStream(".", pipeName, direction, PipeOptions.Asynchronous),
                (pipe, cancellationToken) => pipe.ConnectAsync(cancellationToken)
            );


    /// <summary>
    /// The default <see cref="PipeStreamFactory"/> for creating a <see cref="NamedPipeServerStream"/> instance. The pipe will be asynchronous and data transmitted as stream of bytes rather than messages.
    /// </summary>
    /// <param name="pipeName">The name of the named-pipe.</param>
    /// <param name="direction">The direction of the named-pipe communication from this side. The default is <see cref="E:PipeDirection.Out"/>.</param>
    /// <param name="transmissionMode">The transmission mode of the named-pipe. The default is <see cref="E:PipeTransmissionMode.Byte"/>.</param>
    /// <returns>A factory method which, when called, creates and connects the named-pipe.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pipeName"/> is <see langword="null" />.</exception>
    public static PipeStreamFactory DefaultServerFactory(string pipeName, PipeDirection direction = PipeDirection.Out, PipeTransmissionMode transmissionMode = PipeTransmissionMode.Byte)
        => String.IsNullOrWhiteSpace(pipeName)
            ? throw new ArgumentNullException(nameof(pipeName))
            : CreateFactory(
                () => new NamedPipeServerStream(pipeName, direction, 1, transmissionMode, PipeOptions.Asynchronous),
                (pipe, cancellationToken) => pipe.WaitForConnectionAsync(cancellationToken)
            );


    /// <summary>
    /// Try the specified asyncAction, if it fails, dispose the <paramref name="disposable"/> and rethrow the exception.
    /// </summary>
    /// <typeparam name="TIn">The input object Type, it must be the same type as <typeparamref name="TOut"/> or a sub-class. It must also implement <see cref="IDisposable"/>.</typeparam>
    /// <typeparam name="TOut">The returned object Type, it must be the same type as <typeparamref name="TIn"/> or a base-class/interface.</typeparam>
    /// <param name="disposable">An input object to pass as a parameter to <paramref name="asyncAction"/>. This object will be disposed if <paramref name="asyncAction"/> throws any <see cref="Exception"/>.</param>
    /// <param name="asyncAction">An async action to try, using the provided <paramref name="disposable"/> object.</param>
    /// <returns>The input <paramref name="disposable"/> object cast to the specified <typeparamref name="TOut"/> type.</returns>
    private static async ValueTask<TOut> TryOrDispose<TIn, TOut>(TIn disposable, Func<TIn, Task> asyncAction)
        where TIn : TOut, IDisposable
    {
        try {
            await asyncAction(disposable).ConfigureAwait(false);
            return disposable;
        } catch {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
            if (disposable is IAsyncDisposable asyncDisposable) {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            } else {
                disposable.Dispose();
            }
#else
            disposable.Dispose();
#endif
            throw;
        }
    }

}