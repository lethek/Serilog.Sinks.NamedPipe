using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;

using JetBrains.Annotations;

using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;


namespace Serilog.Sinks.NamedPipe;

/// <summary>
/// A destination for log events that writes to a named pipe.
/// </summary>
public class NamedPipeSink : ILogEventSink, IDisposable
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
    , IAsyncDisposable
#endif
{
    internal NamedPipeSink(PipeStreamFactory pipeFactory, Encoding? encoding, ITextFormatter? formatter, int capacity)
    {
        PipeFactory = pipeFactory;
        Encoding = encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Formatter = formatter ?? new CompactJsonFormatter();

        LogChannel = capacity > 0
            ? Channel.CreateBounded<LogEvent>(new BoundedChannelOptions(capacity) {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleWriter = true,
                SingleReader = true
            })
            : Channel.CreateUnbounded<LogEvent>(new UnboundedChannelOptions {
                SingleWriter = true,
                SingleReader = true
            });

        Worker = Task.Factory.StartNew(
            StartAsyncMessagePump,
            SinkCancellation.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        ).Unwrap();
    }


    /// <summary>Occurs when the message pump is stopped and no more log events will be written to the named pipe.</summary>
    [UsedImplicitly] public event NamedPipeSinkEventHandler? OnMessagePumpStopped;

    /// <summary>Occurs when an error is thrown while running the message pump.</summary>
    [UsedImplicitly] public event NamedPipeSinkErrorEventHandler? OnMessagePumpError;

    /// <summary>Occurs when the <see cref="PipeStream"/> is connected.</summary>
    [UsedImplicitly] public event NamedPipeSinkEventHandler<PipeStream>? OnPipeConnected;

    /// <summary>Occurs when the <see cref="PipeStream"/> is unexpectedly broken, e.g. because the other end of the pipe was closed.</summary>
    [UsedImplicitly] public event NamedPipeSinkEventHandler<PipeStream>? OnPipeBroken;

    /// <summary>Occurs when the <see cref="PipeStream"/> is disconnected for any reason, e.g. because the other end of the pipe was closed, or because the sink is being disposed.</summary>
    [UsedImplicitly] public event NamedPipeSinkEventHandler<PipeStream>? OnPipeDisconnected;

    /// <summary>Occurs when a log event is successfully queued for writing to the named pipe.</summary>
    [UsedImplicitly] public event NamedPipeSinkEventHandler<LogEvent>? OnQueueSuccess;

    /// <summary>Occurs when the queueing of a log event fails, e.g. because the internal buffer is full.</summary>
    [UsedImplicitly] public event NamedPipeSinkEventHandler<LogEvent>? OnQueueFailure;

    /// <summary>Occurs when a write to the named pipe succeeds.</summary>
    [UsedImplicitly] public event NamedPipeSinkEventHandler<LogEvent>? OnWriteSuccess;

    /// <summary>Occurs when a write to the named pipe fails.</summary>
    [UsedImplicitly] public event NamedPipeSinkErrorEventHandler<LogEvent>? OnWriteFailure;


    /// <summary>
    /// Allows easily creating a custom <see cref="PipeStreamFactory"/> for configuring the sink. It consists of two separate steps:
    /// <list type="number">
    /// <item><description>Create a <see cref="PipeStream"/> instance (e.g. <see cref="NamedPipeClientStream"/>, <see cref="NamedPipeServerStream"/>, <see cref="AnonymousPipeClientStream"/>, <see cref="AnonymousPipeServerStream"/>).</description></item>
    /// <item><description>Connect the <see cref="PipeStream"/> instance.</description></item>
    /// </list>
    /// The resulting factory will automatically take care of disposing the <see cref="PipeStream"/> instance internally if the connection fails.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="getStream"></param>
    /// <param name="connect"></param>
    /// <returns></returns>
    public static PipeStreamFactory CreatePipeStreamFactory<T>(Func<T> getStream, Func<T, CancellationToken, Task> connect)
        where T : PipeStream
        => cancellationToken
            => TryOrDispose<T, PipeStream>(
                getStream(),
                x => connect(x, cancellationToken)
            );


    /// <summary>
    /// The default <see cref="PipeStreamFactory"/> for creating a <see cref="NamedPipeClientStream"/> instance. The pipe will be asynchronous and assumed to be located on the local machine.
    /// </summary>
    /// <param name="pipeName">The name of the named-pipe.</param>
    /// <param name="direction">The direction of the named-pipe communication from this side. The default is <see cref="E:PipeDirection.Out"/>.</param>
    /// <returns>A factory method which, when called, creates and connects the named-pipe.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pipeName"/> is <see langword="null" />.</exception>
    public static PipeStreamFactory DefaultClientPipeStreamFactory(string pipeName, PipeDirection direction = PipeDirection.Out)
        => String.IsNullOrWhiteSpace(pipeName)
            ? throw new ArgumentNullException(nameof(pipeName))
            : CreatePipeStreamFactory(
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
    public static PipeStreamFactory DefaultServerPipeStreamFactory(string pipeName, PipeDirection direction = PipeDirection.Out, PipeTransmissionMode transmissionMode = PipeTransmissionMode.Byte)
        => String.IsNullOrWhiteSpace(pipeName)
            ? throw new ArgumentNullException(nameof(pipeName))
            : CreatePipeStreamFactory(
                () => new NamedPipeServerStream(pipeName, direction, 1, transmissionMode, PipeOptions.Asynchronous),
                (pipe, cancellationToken) => pipe.WaitForConnectionAsync(cancellationToken)
            );


    ///<inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        if (LogChannel.Writer.TryWrite(logEvent)) {
            OnQueueSuccess?.Invoke(this, logEvent);
        } else {
            OnQueueFailure?.Invoke(this, logEvent);
        }
    }


    ///<inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }


    ///<inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }


    ///<inheritdoc cref="IDisposable.Dispose" />
    protected virtual void Dispose(bool disposing)
    {
        if (_isDisposed) {
            return;
        }

        if (disposing) {
            try {
                if (!SinkCancellation.IsCancellationRequested) {
                    SinkCancellation.Cancel();
                }
                SinkCancellation.Dispose();
                LogChannel.Writer.Complete();
            } catch {
                //Ignored
            }
        }

        _isDisposed = true;
    }


    /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources asynchronously.</summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_isDisposed) {
            return;
        }

        if (!SinkCancellation.IsCancellationRequested) {
            SinkCancellation.Cancel();
        }
        try {
            await Worker.ConfigureAwait(false);
        } catch {
            //Ignored
        }
        SinkCancellation.Dispose();
        LogChannel.Writer.Complete();

        _isDisposed = true;
    }


    private async Task StartAsyncMessagePump()
    {
        try {
            while (!SinkCancellation.IsCancellationRequested) {
                try {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
                    var pipe = await PipeFactory(SinkCancellation.Token).ConfigureAwait(false);
                    await using var pipeDisposable = pipe.ConfigureAwait(false);
#else
                    using var pipe = await PipeFactory(SinkCancellation.Token).ConfigureAwait(false);
#endif
                    try {
                        OnPipeConnected?.Invoke(this, pipe);

                        //A single emitted LogEvent may require several writes (depending on the Formatter). CoalescingStreamWriter
                        //allows us to batch those writes into a single write that occurs when we manually call Flush/FlushAsync.
                        //It's desirable to write a LogEvent in a single write when the underlying named-pipe is operating in the
                        //PipeTransmissionMode.Message mode. If we don't use a single write per LogEvent, the reader at the other
                        //end of the named-pipe may have difficulty determining where one LogEvent ends and another begins.
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
                        var writer = new CoalescingStreamWriter(pipe, Encoding, leaveOpen: true);
                        await using var writerDisposable = writer.ConfigureAwait(false);
#else
                        using var writer = new CoalescingStreamWriter(pipe, Encoding, leaveOpen: true);
#endif

                        while (pipe.IsConnected && await LogChannel.Reader.WaitToReadAsync(SinkCancellation.Token).ConfigureAwait(false)) {
                            if (LogChannel.Reader.TryPeek(out var logEvent)) {
                                try {
                                    //Write the LogEvent to the CoalescingTextWriter
                                    Formatter.Format(logEvent, writer);

                                    //Flush the CoalescingTextWriter to ensure the entire logEvent is written to the pipe using a single Write
                                    await writer.FlushAsync(SinkCancellation.Token).ConfigureAwait(false);

                                    //Now logEvent has been successfully written to the pipe, we can remove it from the queue
                                    LogChannel.Reader.TryRead(out _);

                                    //Notify listeners that the write was successful
                                    OnWriteSuccess?.Invoke(this, logEvent);

                                } catch (Exception ex) {
                                    SelfLog.WriteLine($"Write failure: {ex}");
                                    OnWriteFailure?.Invoke(this, logEvent, ex);
                                    throw;
                                }
                            }
                        }

                    } catch (Exception ex) when (ex is not OperationCanceledException) {
                        OnPipeBroken?.Invoke(this, pipe);
                    } finally {
                        OnPipeDisconnected?.Invoke(this, pipe);
                    }

                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    SelfLog.WriteLine($"Error in message-pump (pump may retry and continue): {ex}");
                    OnMessagePumpError?.Invoke(this, ex);
                }
            }

        } catch (Exception ex) when (ex is not OperationCanceledException) {
            SelfLog.WriteLine($"Fatal error in message-pump (pump will terminate): {ex}");
            OnMessagePumpError?.Invoke(this, ex);

        } finally {
            OnMessagePumpStopped?.Invoke(this);
        }
    }


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


    internal protected readonly CancellationTokenSource SinkCancellation = new();
    internal protected readonly ITextFormatter Formatter;
    internal protected readonly Channel<LogEvent> LogChannel;
    internal protected readonly PipeStreamFactory PipeFactory;
    internal protected readonly Encoding Encoding;
    internal protected readonly Task Worker;


    private bool _isDisposed;
}
