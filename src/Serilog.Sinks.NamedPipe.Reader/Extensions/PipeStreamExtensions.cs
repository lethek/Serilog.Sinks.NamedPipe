using System.Buffers;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Serilog.Sinks.NamedPipe.Reader.Extensions;

/// <summary>
/// Provides some extension methods for <see cref="PipeStream"/> to simplify reading messages from it.
/// </summary>
public static class PipeStreamExtensions
{
    /// <summary>
    /// Asynchronously reads a message from a pipe stream and returns its byte representation stored in a read-only memory block.
    /// The message is read as a whole, so the method will not return until the whole message is read.
    /// The named-pipe must be in <see cref="E:PipeTransmissionMode.Message"/> mode.
    /// </summary>
    /// <param name="pipe">The <see cref="PipeStream"/> instance to read from.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous read operation that wraps a read-only memory block of the read message's bytes, or null if there is no message to read.</returns>
    public static async ValueTask<ReadOnlyMemory<byte>?> ReadMessageAsync(this PipeStream pipe, CancellationToken cancellationToken = default)
    {
        if (pipe.ReadMode != PipeTransmissionMode.Message) {
            throw new InvalidOperationException("ReadMode is not of PipeTransmissionMode.Message.");
        }

        using var memoryStream = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSize);

        try {
            do {
#if NETSTANDARD2_0
                var bytesRead = await pipe.ReadAsync(buffer, 0, DefaultBufferSize, cancellationToken).ConfigureAwait(false);
                if (bytesRead > 0) {
                    memoryStream.Write(buffer, 0, bytesRead);
                }
#else
                var bytesRead = await pipe.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (bytesRead > 0) {
                    memoryStream.Write(buffer.AsSpan()[..bytesRead]);
                }
#endif
            } while (!pipe.IsMessageComplete);
        } finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return memoryStream.Length == 0
            ? null
            : memoryStream.TryGetBuffer(out var bufferSegment)
                ? bufferSegment.AsMemory()
                : memoryStream.ToArray().AsMemory();
    }


    /// <summary>
    /// Reads a message from a pipe stream and returns its byte representation stored in a read-only memory block.
    /// The message is read as a whole, so the method will not return until the whole message is read.
    /// The named-pipe must be in <see cref="E:PipeTransmissionMode.Message"/> mode.
    /// </summary>
    /// <param name="pipe">The <see cref="PipeStream"/> instance to read from.</param>
    /// <returns>A read-only memory block of the read message's bytes, or null if there is no message to read.</returns>
    public static ReadOnlyMemory<byte>? ReadMessage(this PipeStream pipe)
    {
        if (pipe.ReadMode != PipeTransmissionMode.Message) {
            throw new InvalidOperationException("ReadMode is not of PipeTransmissionMode.Message.");
        }

        using var memoryStream = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSize);

        try {
            do {
#if NETSTANDARD2_0
                var bytesRead = pipe.Read(buffer, 0, DefaultBufferSize);
                if (bytesRead > 0) {
                    memoryStream.Write(buffer, 0, bytesRead);
                }
#else
                var bytesRead = pipe.Read(buffer.AsSpan());
                if (bytesRead > 0) {
                    memoryStream.Write(buffer.AsSpan()[..bytesRead]);
                }
#endif
            } while (!pipe.IsMessageComplete);
        } finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return memoryStream.Length == 0
            ? null
            : memoryStream.TryGetBuffer(out var bufferSegment)
                ? bufferSegment.AsMemory()
                : memoryStream.ToArray().AsMemory();
    }


    /// <summary>
    /// Asynchronously reads a message from a pipe stream and returns its byte representation stored in a read-only memory block.
    /// The message is read as a whole, so the method will not return until the whole message is read.
    /// The named-pipe must be in <see cref="E:PipeTransmissionMode.Message"/> mode.
    /// </summary>
    /// <param name="pipe">The <see cref="PipeStream"/> instance to read from.</param>
    /// <param name="encoding">Character encoding to use when reading from the <paramref name="pipe"/>. The default is UTF-8 without BOM.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous read operation that wraps a string of the read message with the applied <paramref name="encoding"/>, or <see langword="null"/> if there is no message to read.</returns>
    public static async ValueTask<string?> ReadMessageStringAsync(this PipeStream pipe, Encoding? encoding = null, CancellationToken cancellationToken = default)
    {
        var msg = await pipe.ReadMessageAsync(cancellationToken);
        return msg.HasValue
            ? GetStringFromBuffer(msg.Value, encoding ?? DefaultEncoding)
            : null;
    }


    /// <summary>
    /// Reads a message from a pipe stream and returns its raw string representation using the specified <paramref name="encoding"/>.
    /// The message is read as a whole, so the method will not return until the whole message is read.
    /// The named-pipe must be in <see cref="E:PipeTransmissionMode.Message"/> mode.
    /// </summary>
    /// <param name="pipe">The <see cref="PipeStream"/> instance to read from.</param>
    /// <param name="encoding">Character encoding to use when reading from the <paramref name="pipe"/>. The default is UTF-8 without BOM.</param>
    /// <returns>A string representing the read message with the applied <paramref name="encoding"/>, or <see langword="null"/> if there is no message to read.</returns>
    public static string? ReadMessageString(this PipeStream pipe, Encoding? encoding = null)
    {
        var msg = pipe.ReadMessage();
        return msg.HasValue
            ? GetStringFromBuffer(msg.Value, encoding ?? DefaultEncoding)
            : null;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetStringFromBuffer(ReadOnlyMemory<byte> buffer, Encoding encoding)
#if NETSTANDARD2_0
        => MemoryMarshal.TryGetArray(buffer, out var segment)
            ? encoding.GetString(segment.Array!, segment.Offset, segment.Count)
            : encoding.GetString(buffer.ToArray());
#else
        => encoding.GetString(buffer.Span);
#endif


    private static readonly Encoding DefaultEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private const int DefaultBufferSize = 2048;
}
