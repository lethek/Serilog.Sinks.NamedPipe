using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;

using Serilog.Events;
using Serilog.Formatting.Compact.Reader;
using Serilog.Sinks.NamedPipe.Reader.Extensions;


namespace Serilog.Sinks.NamedPipe.Reader;

/// <summary>
/// Provides functionality for reading log events asynchronously from a named pipe.
/// </summary>
public class NamedPipeAsyncReader
{
    /// <summary>
    /// Enumerates all log events asynchronously from a named pipe. When deserializing, this method assumes that each log event was formatted using the
    /// CompactJsonFormatter (which is the default formatter used by NamedPipeSink).
    /// </summary>
    /// <param name="factory">A factory method for creating a <see cref="PipeStream"/> and opening its connection."></param>
    /// <param name="encoding">Character encoding to use when reading from a named pipe. The default is UTF-8 without BOM.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>An asynchronous enumerable that yields Serilog <see cref="LogEvent"/> instances.</returns>
    public static IAsyncEnumerable<LogEvent> ReadAllLogEventsAsync(PipeStreamFactory factory, Encoding? encoding = null, CancellationToken cancellationToken = default)
        => new NamedPipeAsyncReader(factory, encoding).ReadAllLogEventsAsync(cancellationToken);


    /// <summary>
    /// <para>Enumerates all lines/messages asynchronously from a named pipe.</para>
    /// </summary>
    /// <param name="factory">A factory method for creating a <see cref="PipeStream"/> and opening its connection."></param>
    /// <param name="encoding">Character encoding to use when reading from a named pipe. The default is UTF-8 without BOM.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>An asynchronous enumerable that yields lines/messages read from the named pipe.</returns>
    public static IAsyncEnumerable<string> ReadAllLinesAsync(PipeStreamFactory factory, Encoding? encoding = null, CancellationToken cancellationToken = default)
        => new NamedPipeAsyncReader(factory, encoding).ReadAllLinesAsync(cancellationToken);


    /// <summary>
    /// Initializes a new instance of the <see cref="NamedPipeAsyncReader"/> class.
    /// </summary>
    /// <param name="pipeStreamFactory">A factory method for creating a <see cref="PipeStream"/> and opening its connection.</param>
    /// <param name="encoding">Character encoding to use when reading from a named pipe. The default is UTF-8 without BOM.</param>
    public NamedPipeAsyncReader(PipeStreamFactory pipeStreamFactory, Encoding? encoding = null)
    {
        _pipeStreamFactory = pipeStreamFactory ?? throw new ArgumentNullException(nameof(pipeStreamFactory));
        _encoding = encoding ?? DefaultEncoding;
    }


    /// <summary>
    /// Enumerates all log events asynchronously from a named pipe. When deserializing, this method assumes that each log event was formatted using the
    /// CompactJsonFormatter (which is the default formatter used by NamedPipeSink).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    /// <returns>An asynchronous enumerable that yields Serilog <see cref="LogEvent" /> instances deserialized from a named pipe.</returns>
    public async IAsyncEnumerable<LogEvent> ReadAllLogEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var msg in ReadAllLinesAsync(cancellationToken).ConfigureAwait(false)) {
            yield return LogEventReader.ReadFromString(msg);
        }
    }


    /// <summary>
    /// <para>Enumerates all lines/messages asynchronously from a named pipe.</para>
    /// <para>Under default conditions, each string returned will correspond to a single raw JSON formatted log event.</para>
    /// <para>If the pipe is using <see cref="E:PipeTransmissionMode.Message"/> transmission mode this is guaranteed, however
    /// that mode is only available on Windows systems.</para>
    /// <para>If the pipe is using the default <see cref="E:PipeTransmissionMode.Byte"/> transmission mode AND the sink was
    /// configured with a custom formatter which doesn't escape newline characters, then strings returned by this method may not
    /// always correspond to a single log event. In this case, the caller is responsible for parsing the returned strings into
    /// whatever format is required.</para>
    /// </summary>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the read operation.</param>
    /// <returns>An asynchronous enumerable that yields lines/messages read from a named pipe.</returns>
    public async IAsyncEnumerable<string> ReadAllLinesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested) {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
            var pipe = await _pipeStreamFactory(cancellationToken).ConfigureAwait(false);
            await using var pipeDisposable = pipe.ConfigureAwait(false);
#else
            using var pipe = await _pipeStreamFactory(cancellationToken).ConfigureAwait(false);
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


    private readonly PipeStreamFactory _pipeStreamFactory;
    private readonly Encoding _encoding;


    private static readonly Encoding DefaultEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
