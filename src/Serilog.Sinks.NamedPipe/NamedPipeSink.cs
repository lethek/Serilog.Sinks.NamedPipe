using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;

using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;


namespace Serilog.Sinks.NamedPipe;

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
            StartAsyncLogEventPump,
            SinkCancellation.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        ).Unwrap();
    }


    public static PipeStreamFactory CreateNamedPipeClientFactory(string pipeName)
    {
        if (String.IsNullOrWhiteSpace(pipeName)) {
            throw new ArgumentNullException(nameof(pipeName));
        }
        return async cancellationToken => {
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return pipe;
        };
    }


    public static PipeStreamFactory CreateNamedPipeServerFactory(string pipeName)
    {
        if (String.IsNullOrWhiteSpace(pipeName)) {
            throw new ArgumentNullException(nameof(pipeName));
        }
        return async cancellationToken => {
            var pipe = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            return pipe;
        };
    }


    public void Emit(LogEvent logEvent)
        => Channel.Writer.TryWrite(logEvent);


    private async Task StartAsyncLogEventPump()
    {
        try {
            while (!SinkCancellation.Token.IsCancellationRequested) {
                using var pipe = await PipeFactory(SinkCancellation.Token).ConfigureAwait(false);
                try {
                    using var output = new StreamWriter(pipe, Encoding) { AutoFlush = true };
                    while (await Channel.Reader.WaitToReadAsync(SinkCancellation.Token).ConfigureAwait(false)) {
                        if (Channel.Reader.TryPeek(out var logEvent)) {
                            Formatter.Format(logEvent, output);
                            Channel.Reader.TryRead(out _);
                        }
                    }
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    SelfLog.WriteLine("Broken pipe");
                }
            }
        } catch (OperationCanceledException) {
            //Cancellation has been signalled, ignore the exception and just exit the pump
        } catch (Exception ex) {
            SelfLog.WriteLine("Unable to continue writing log events to named pipe: {0}", ex);
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


    protected internal readonly CancellationTokenSource SinkCancellation = new();
    protected internal readonly ITextFormatter Formatter;
    protected internal readonly Channel<LogEvent> Channel;
    protected internal readonly PipeStreamFactory PipeFactory;
    protected internal readonly Encoding Encoding;
    protected internal readonly Task Worker;
}