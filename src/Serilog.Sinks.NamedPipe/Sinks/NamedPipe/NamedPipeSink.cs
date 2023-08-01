// ReSharper disable EventNeverSubscribedTo.Global
using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;

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
    public event NamedPipeSinkEventHandler? OnMessagePumpStopped;

    /// <summary>Occurs when an error is thrown while running the message pump.</summary>
    public event NamedPipeSinkErrorEventHandler? OnMessagePumpError;

    /// <summary>Occurs when the <see cref="PipeStream"/> is connected.</summary>
    public event NamedPipeSinkEventHandler<PipeStream>? OnPipeConnected;

    /// <summary>Occurs when the <see cref="PipeStream"/> is unexpectedly broken, e.g. because the other end of the pipe was closed.</summary>
    public event NamedPipeSinkEventHandler<PipeStream>? OnPipeBroken;

    /// <summary>Occurs when the <see cref="PipeStream"/> is disconnected for any reason, e.g. because the other end of the pipe was closed, or because the sink is being disposed.</summary>
    public event NamedPipeSinkEventHandler<PipeStream>? OnPipeDisconnected;

    /// <summary>Occurs when a log event is successfully queued for writing to the named pipe.</summary>
    public event NamedPipeSinkEventHandler<LogEvent>? OnQueueSuccess;

    /// <summary>Occurs when the queueing of a log event fails, e.g. because the internal buffer is full.</summary>
    public event NamedPipeSinkEventHandler<LogEvent>? OnQueueFailure;

    /// <summary>Occurs when a write to the named pipe succeeds.</summary>
    public event NamedPipeSinkEventHandler<LogEvent>? OnWriteSuccess;

    /// <summary>Occurs when a write to the named pipe fails.</summary>
    public event NamedPipeSinkErrorEventHandler<LogEvent>? OnWriteFailure;


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
