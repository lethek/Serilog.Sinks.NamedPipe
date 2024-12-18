using System.Text;


namespace Serilog.Sinks.NamedPipe;

internal sealed class CoalescingStreamWriter(Stream stream, Encoding encoding, bool leaveOpen = false) : TextWriter
{
    public override Encoding Encoding { get; } = encoding;


    public override void Write(char value)
        => _buffer.Append(value);


    public override void Write(string? value)
        => _buffer.Append(value);


    public override void Write(char[] buffer, int index, int count)
        => _buffer.Append(buffer, index, count);


#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
    public override void Write(ReadOnlySpan<char> buffer)
        => _buffer.Append(buffer);
#endif


    public override void Flush()
    {
        if (_buffer.Length > 0) {
            var bytes = Encoding.GetBytes(_buffer.ToString());
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
            stream.Write(bytes.AsSpan());
#else
            stream.Write(bytes, 0, bytes.Length);
#endif
            stream.Flush();
            _buffer.Clear();
        }
    }

    
    public override Task WriteAsync(char value)
    {
        _buffer.Append(value);
        return Task.CompletedTask;
    }


    public override Task WriteAsync(string? value)
    {
        _buffer.Append(value);
        return Task.CompletedTask;
    }


    public override Task FlushAsync()
        => FlushAsyncCore(default);


#if NET8_0_OR_GREATER
    public override Task FlushAsync(CancellationToken cancellationToken)
#else
    public Task FlushAsync(CancellationToken cancellationToken)
#endif
        => FlushAsyncCore(cancellationToken);


    public async Task FlushAsyncCore(CancellationToken cancellationToken)
    {
        if (_buffer.Length == 0) {
            return;
        }

        var bytes = Encoding.GetBytes(_buffer.ToString());
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
        await stream.WriteAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
#else
        await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
#endif
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        _buffer.Clear();
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            Flush();
            if (!leaveOpen) {
                stream.Dispose();
            }
        }
        base.Dispose(disposing);
    }


    private readonly StringBuilder _buffer = new();
}