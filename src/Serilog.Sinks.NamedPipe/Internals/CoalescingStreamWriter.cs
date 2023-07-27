using System.Text;

namespace Serilog.Sinks.NamedPipe.Internals;

internal sealed class CoalescingStreamWriter : TextWriter
{
    public CoalescingStreamWriter(Stream stream, Encoding encoding, bool leaveOpen = false)
    {
        _buffer = new StringBuilder();
        _innerStream = stream;
        _leaveOpen = leaveOpen;
        Encoding = encoding;
    }


    public override Encoding Encoding { get; }


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
            _innerStream.Write(bytes.AsSpan());
#else
            _innerStream.Write(bytes, 0, bytes.Length);
#endif
            _innerStream.Flush();

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


    public override async Task FlushAsync()
    {
        if (_buffer.Length > 0) {
            var bytes = Encoding.GetBytes(_buffer.ToString());
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
            await _innerStream.WriteAsync(bytes.AsMemory(), default);
#else
            await _innerStream.WriteAsync(bytes, 0, bytes.Length, default);
#endif
            await _innerStream.FlushAsync(default);
            _buffer.Clear();
        }
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            Flush();
            if (!_leaveOpen) {
                _innerStream.Dispose();
            }
        }
        base.Dispose(disposing);
    }


    private readonly StringBuilder _buffer;
    private readonly Stream _innerStream;
    private readonly bool _leaveOpen;
}