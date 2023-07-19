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
        _pipeFactory = pipeFactory;
        _encoding = encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        _formatter = formatter ?? new CompactJsonFormatter();

        _channel = (capacity > 0)
            ? Channel.CreateBounded<LogEvent>(new BoundedChannelOptions(capacity) {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleWriter = true,
                SingleReader = true
            })
            : Channel.CreateUnbounded<LogEvent>(new UnboundedChannelOptions {
                SingleWriter = true,
                SingleReader = true
            });

        Task.Factory.StartNew(StartAsyncLogEventPump, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
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
        => _channel.Writer.TryWrite(logEvent);


    private async Task StartAsyncLogEventPump()
    {
        try {
            while (!_sinkCancellation.Token.IsCancellationRequested) {
                using var pipe = await _pipeFactory(_sinkCancellation.Token).ConfigureAwait(false);
                try {
                    using var output = new StreamWriter(pipe, _encoding) { AutoFlush = true };
                    while (await _channel.Reader.WaitToReadAsync(_sinkCancellation.Token).ConfigureAwait(false)) {
                        if (_channel.Reader.TryPeek(out var logEvent)) {
                            _formatter.Format(logEvent, output);
                            _channel.Reader.TryRead(out _);
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
            _sinkCancellation.Cancel();
            _sinkCancellation.Dispose();
            _channel.Writer.Complete();
        } catch {
            //Ignored
        }
    }


    private readonly CancellationTokenSource _sinkCancellation = new();
    private readonly ITextFormatter _formatter;
    private readonly Channel<LogEvent> _channel;
    private readonly PipeStreamFactory _pipeFactory;
    private readonly Encoding _encoding;
}