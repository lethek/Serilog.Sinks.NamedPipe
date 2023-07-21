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

        Channel = (capacity > 0)
            ? System.Threading.Channels.Channel.CreateBounded<LogEvent>(new BoundedChannelOptions(capacity) {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleWriter = true,
                SingleReader = true
            })
            : System.Threading.Channels.Channel.CreateUnbounded<LogEvent>(new UnboundedChannelOptions {
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
    [UsedImplicitly] public event NamedPipeSinkEventHandler<PipeStream>? OnPipeConnected;
    [UsedImplicitly] public event NamedPipeSinkEventHandler<PipeStream>? OnPipeBroken;
    [UsedImplicitly] public event NamedPipeSinkEventHandler<PipeStream>? OnPipeDisconnected;
    [UsedImplicitly] public event NamedPipeSinkEventHandler<LogEvent>? OnQueueSuccess;
    [UsedImplicitly] public event NamedPipeSinkEventHandler<LogEvent>? OnQueueFailure;
    [UsedImplicitly] public event NamedPipeSinkEventHandler<LogEvent>? OnWriteSuccess;
    [UsedImplicitly] public event NamedPipeSinkErrorEventHandler<LogEvent>? OnWriteFailure;


    public static PipeStreamFactory CreateNamedPipeClientFactory(string pipeName, PipeDirection direction = PipeDirection.InOut)
    {
        if (String.IsNullOrWhiteSpace(pipeName)) {
            throw new ArgumentNullException(nameof(pipeName));
        }
        return async cancellationToken => {
            var pipe = new NamedPipeClientStream(".", pipeName, direction, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return pipe;
        };
        
    }


    public static PipeStreamFactory CreateNamedPipeServerFactory(string pipeName, PipeDirection direction = PipeDirection.InOut, PipeTransmissionMode transmissionMode = PipeTransmissionMode.Byte)
    {
        if (String.IsNullOrWhiteSpace(pipeName)) {
            throw new ArgumentNullException(nameof(pipeName));
        }
        return async cancellationToken => {
            var pipe = new NamedPipeServerStream(pipeName, direction, 1, transmissionMode, PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            return pipe;
        };
    }


    public void Emit(LogEvent logEvent)
    {
        if (Channel.Writer.TryWrite(logEvent)) {
            OnQueueSuccess?.Invoke(this, logEvent);
        } else {
            OnQueueFailure?.Invoke(this, logEvent);
        }
    }


    private async Task StartAsyncMessagePump()
    {
        try {
            while (!SinkCancellation.Token.IsCancellationRequested) {
                using var pipe = await PipeFactory(SinkCancellation.Token).ConfigureAwait(false);
                try {
                    OnPipeConnected?.Invoke(this, pipe);
                    using var pipeWriter = new StreamWriter(pipe, Encoding) { AutoFlush = true };

                    //A single emitted LogEvent may require several writes (depending on the Formatter). CoalescingTextWriter
                    //allows us to batch those writes into a single write that occurs when we manually call Flush/FlushAsync.
                    //It's desirable to write a LogEvent in a single write when the underlying named-pipe is operating in the
                    //PipeTransmissionMode.Message mode. If we don't use a single write per LogEvent, the reader at the other
                    //end of the named-pipe may have difficulty determining where one LogEvent ends and another begins.
                    using var writer = new CoalescingTextWriter(pipeWriter);

                    while (await Channel.Reader.WaitToReadAsync(SinkCancellation.Token).ConfigureAwait(false)) {
                        if (Channel.Reader.TryPeek(out var logEvent)) {
                            try {
                                Formatter.Format(logEvent, writer);
                                //Flush the CoalescingTextWriter to ensure the entire logEvent is written to the pipe using a single Write
                                await writer.FlushAsync();
                                //Now logEvent has been successfully written to the pipe, we can remove it from the queue
                                Channel.Reader.TryRead(out _);
                                OnWriteSuccess?.Invoke(this, logEvent);
                            } catch (Exception ex) {
                                OnWriteFailure?.Invoke(this, logEvent, ex);
                                throw;
                            }
                        }
                    }
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    SelfLog.WriteLine("Broken pipe");
                    OnPipeBroken?.Invoke(this, pipe);
                } finally {
                    OnPipeDisconnected?.Invoke(this, pipe);
                }
            }
        } catch (OperationCanceledException) {
            //Cancellation has been signalled, ignore the exception and just exit the pump
        } catch (Exception ex) {
            SelfLog.WriteLine("Unable to continue writing log events to named pipe: {0}", ex);
        } finally {
            OnMessagePumpStopped?.Invoke(this);
        }
    }


    public void Dispose()
    {
        try {
            SinkCancellation.Cancel();
            SinkCancellation.Dispose();
            Channel.Writer.Complete();
        } catch {
            //Ignored
        }
    }
    

    internal protected readonly CancellationTokenSource SinkCancellation = new();
    internal protected readonly ITextFormatter Formatter;
    internal protected readonly Channel<LogEvent> Channel;
    internal protected readonly PipeStreamFactory PipeFactory;
    internal protected readonly Encoding Encoding;
    internal protected readonly Task Worker;
}
