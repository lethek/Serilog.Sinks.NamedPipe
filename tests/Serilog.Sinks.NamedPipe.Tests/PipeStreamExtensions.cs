using System.Buffers;
using System.IO.Pipes;
using System.Text;

using Microsoft.Extensions.ObjectPool;


namespace Serilog.Sinks.NamedPipe.Tests;

public static class PipeStreamExtensions
{
    public static async Task<string> ReadMessageStringAsync(this PipeStream pipe, Encoding? encoding = null, CancellationToken cancellationToken = default)
    {
        if (pipe.ReadMode != PipeTransmissionMode.Message) {
            throw new InvalidOperationException("ReadMode is not of PipeTransmissionMode.Message.");
        }

        using var reader = new StreamReader(pipe, encoding ?? Encoding.UTF8, leaveOpen: true);

        var buffer = ArrayPool<char>.Shared.Rent(4096);
        var messageBuilder = StringBuilderPool.Get();
        try {
            while (true) {
                int bytesRead = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (bytesRead == 0) {
                    break;
                }

                messageBuilder.Append(buffer.AsSpan().Slice(0, bytesRead));

                if (pipe.IsMessageComplete) {
                    break;
                }
            }

            return messageBuilder.ToString();

        } finally {
            ArrayPool<char>.Shared.Return(buffer);
            StringBuilderPool.Return(messageBuilder);
        }
    }


    private static ObjectPoolProvider ObjectPoolProvider = new DefaultObjectPoolProvider();
    private static ObjectPool<StringBuilder> StringBuilderPool = ObjectPoolProvider.CreateStringBuilderPool();
}