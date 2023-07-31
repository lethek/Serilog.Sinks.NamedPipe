using System.Buffers;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Serilog.Sinks.NamedPipe.Reader.Extensions;

public static class PipeStreamExtensions
{
    public static async ValueTask<Memory<byte>?> ReadMessageAsync(this PipeStream pipe, CancellationToken cancellationToken = default)
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


    public static Memory<byte>? ReadMessage(this PipeStream pipe)
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


    public static async ValueTask<string?> ReadMessageStringAsync(this PipeStream pipe, Encoding? encoding = null, CancellationToken cancellationToken = default)
    {
        var msg = await ReadMessageAsync(pipe, cancellationToken);
        return msg.HasValue
            ? GetStringFromBuffer(msg.Value, encoding ?? DefaultEncoding)
            : null;
    }


    public static string? ReadMessageString(this PipeStream pipe, Encoding? encoding = null)
    {
        var msg = ReadMessage(pipe);
        return msg.HasValue
            ? GetStringFromBuffer(msg.Value, encoding ?? DefaultEncoding)
            : null;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetStringFromBuffer(Memory<byte> buffer, Encoding encoding)
#if NETSTANDARD2_0
        => MemoryMarshal.TryGetArray<byte>(buffer, out var segment)
            ? encoding.GetString(segment.Array!, segment.Offset, segment.Count)
            : encoding.GetString(buffer.ToArray());
#else
        => encoding.GetString(buffer.Span);
#endif


    private static readonly Encoding DefaultEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private const int DefaultBufferSize = 2048;
}
