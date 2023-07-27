using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;

using JetBrains.Annotations;

using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;


namespace Serilog.Sinks.NamedPipe.Internals;

internal class NamedPipeSink : ILogEventSink, IDisposable
{
    internal NamedPipeSink(PipeStreamFactory pipeFactory, Encoding? encoding, ITextFormatter? formatter, int capacity)
    {
        PipeFactory = pipeFactory;
        Encoding = encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Formatter = formatter ?? new CompactJsonFormatter();

        LogChannel = (capacity > 0)
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


    [UsedImplicitly] public event NamedPipeSinkEventHandler? OnMessagePumpStopped;
    [UsedImplicitly] public event NamedPipeSinkErrorEventHandler? OnMessagePumpError;
    [UsedImplicitly] public event NamedPipeSinkEventHandler<PipeStream>? OnPipeConnected;
    [UsedImplicitly] public event NamedPipeSinkEventHandler<PipeStream>? OnPipeBroken;
    [UsedImplicitly] public event NamedPipeSinkEventHandler<PipeStream>? OnPipeDisconnected;
    [UsedImplicitly] public event NamedPipeSinkEventHandler<LogEvent>? OnQueueSuccess;
    [UsedImplicitly] public event NamedPipeSinkEventHandler<LogEvent>? OnQueueFailure;
    [UsedImplicitly] public event NamedPipeSinkEventHandler<LogEvent>? OnWriteSuccess;
    [UsedImplicitly] public event NamedPipeSinkErrorEventHandler<LogEvent>? OnWriteFailure;



    public static PipeStreamFactory NamedPipeClientConnectionFactory(string pipeName, PipeDirection direction = PipeDirection.Out)
    {
        if (String.IsNullOrWhiteSpace(pipeName)) {
            throw new ArgumentNullException(nameof(pipeName));
        }
        return async cancellationToken => {
            var pipe = new NamedPipeClientStream(".", pipeName, direction, PipeOptions.Asynchronous);
            try {
                await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
                return pipe;
            } catch (Exception) {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
                await pipe.DisposeAsync().ConfigureAwait(false);
#else
                pipe.Dispose();
#endif
                throw;
            }
        };
    }


    public static PipeStreamFactory NamedPipeServerConnectionFactory(string pipeName, PipeDirection direction = PipeDirection.Out, PipeTransmissionMode transmissionMode = PipeTransmissionMode.Byte)
    {
        if (String.IsNullOrWhiteSpace(pipeName)) {
            throw new ArgumentNullException(nameof(pipeName));
        }
        return async cancellationToken => {
            var pipe = new NamedPipeServerStream(pipeName, direction, 1, transmissionMode, PipeOptions.Asynchronous);
            try {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                return pipe;
            } catch (Exception) {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
                await pipe.DisposeAsync().ConfigureAwait(false);
#else
                pipe.Dispose();
#endif
                throw;
            }
        };
    }


    public void Emit(LogEvent logEvent)
    {
        if (LogChannel.Writer.TryWrite(logEvent)) {
            OnQueueSuccess?.Invoke(this, logEvent);
        } else {
            OnQueueFailure?.Invoke(this, logEvent);
        }
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


    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }


    protected virtual void Dispose(bool disposing)
    {
        if (_isDisposed) {
            return;
        }

        if (disposing) {
            try {
                SinkCancellation.Cancel();
                SinkCancellation.Dispose();
                LogChannel.Writer.Complete();
            } catch {
                //Ignored
            }
        }
        _isDisposed = true;
    }


    internal protected readonly CancellationTokenSource SinkCancellation = new();
    internal protected readonly ITextFormatter Formatter;
    internal protected readonly Channel<LogEvent> LogChannel;
    internal protected readonly PipeStreamFactory PipeFactory;
    internal protected readonly Encoding Encoding;
    internal protected readonly Task Worker;


    private bool _isDisposed;
}
