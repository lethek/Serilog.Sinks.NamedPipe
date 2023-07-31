using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;

using Serilog.Events;
using Serilog.Formatting.Compact.Reader;
using Serilog.Sinks.NamedPipe.Reader.Extensions;


namespace Serilog.Sinks.NamedPipe.Reader;

public class NamedPipeAsyncReader
{

    public NamedPipeAsyncReader(PipeStreamFactory factory, Encoding? encoding = null)
    {
        _factory = factory;
        _encoding = encoding ?? DefaultEncoding;
    }


    public async IAsyncEnumerable<LogEvent> ReadAllAsync<T>([EnumeratorCancellation] CancellationToken cancellationToken = default)
        where T : LogEvent
    {
        await foreach (var msg in ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
            yield return LogEventReader.ReadFromString(msg);
        }
    }


    public async IAsyncEnumerable<string> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested) {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
            var pipe = await _factory(cancellationToken).ConfigureAwait(false);
            await using var pipeDisposable = pipe.ConfigureAwait(false);
#else
            using var pipe = await _factory(cancellationToken).ConfigureAwait(false);
#endif

            if (pipe.ReadMode == PipeTransmissionMode.Message) {
                while (pipe.IsConnected) {
                    var message = await pipe.ReadMessageStringAsync(_encoding, cancellationToken).ConfigureAwait(false);
                    if (message != null) {
                        yield return message;
                    }
                }

            } else {
                using var reader = new StreamReader(pipe, _encoding, false, -1, true);
                while (pipe.IsConnected && !reader.EndOfStream) {
                    cancellationToken.ThrowIfCancellationRequested();
#if NET7_0_OR_GREATER
                    var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
#else
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
#endif
                    if (line != null) {
                        yield return line;
                    }
                }
            }
        }
    }


    private readonly PipeStreamFactory _factory;
    private readonly Encoding _encoding;


    private static readonly Encoding DefaultEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
