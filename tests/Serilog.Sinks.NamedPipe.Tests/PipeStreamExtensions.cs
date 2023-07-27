using System.Buffers;
using System.IO.Pipes;
using System.Text;


namespace Serilog.Sinks.NamedPipe.Tests;

public static class PipeStreamExtensions
{
    public static async ValueTask<string?> ReadMessageStringAsync(this PipeStream pipe, Encoding? encoding = null, CancellationToken cancellationToken = default)
    {
        if (pipe.ReadMode != PipeTransmissionMode.Message) {
            throw new InvalidOperationException("ReadMode is not of PipeTransmissionMode.Message.");
        }

        encoding ??= UTF8NoBOMEncoding;
        using var memoryStream = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSize);

        try {
            do {
                var bytesRead = await pipe.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (bytesRead > 0) {
                    memoryStream.Write(buffer.AsSpan()[..bytesRead]);
                }
            } while (!pipe.IsMessageComplete);
        } finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return memoryStream.Length == 0
            ? null
            : memoryStream.TryGetBuffer(out var bufferSegment)
                ? encoding.GetString(bufferSegment.AsSpan())
                : encoding.GetString(memoryStream.ToArray());
    }


    public static string? ReadMessageString(this PipeStream pipe, Encoding? encoding = null)
    {
        if (pipe.ReadMode != PipeTransmissionMode.Message) {
            throw new InvalidOperationException("ReadMode is not of PipeTransmissionMode.Message.");
        }

        encoding ??= UTF8NoBOMEncoding;
        using var memoryStream = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSize);

        try {
            do {
                var bytesRead = pipe.Read(buffer.AsSpan());
                if (bytesRead > 0) {
                    memoryStream.Write(buffer.AsSpan()[..bytesRead]);
                }
            } while (!pipe.IsMessageComplete);
        } finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return memoryStream.Length == 0
            ? null
            : memoryStream.TryGetBuffer(out var bufferSegment)
                ? encoding.GetString(bufferSegment.AsSpan())
                : encoding.GetString(memoryStream.ToArray());
    }


    private static readonly Encoding UTF8NoBOMEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private const int DefaultBufferSize = 2048;
}