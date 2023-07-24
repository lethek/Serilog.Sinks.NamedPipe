using System.Text;

namespace Serilog.Sinks.NamedPipe.Internals;

internal class CoalescingTextWriter : TextWriter
{
    public CoalescingTextWriter(TextWriter innerWriter)
    {
        _innerWriter = innerWriter;
        _buffer = new StringBuilder();
    }


    public override Encoding Encoding => _innerWriter.Encoding;


    public override void Write(char value)
        => _buffer.Append(value);


    public override void Write(string? value)
        => _buffer.Append(value);


    public override void Flush()
    {
        if (_buffer.Length > 0) {
#if NETCOREAPP3_0_OR_GREATER
            _innerWriter.Write(_buffer);
#else
            _innerWriter.Write(_buffer.ToString());
#endif
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
#if NETCOREAPP3_0_OR_GREATER
            await _innerWriter.WriteAsync(_buffer);
#else
            await _innerWriter.WriteAsync(_buffer.ToString());
#endif
            _buffer.Clear();
        }
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            Flush();
            _innerWriter.Dispose();
        }
        base.Dispose(disposing);
    }


    private readonly StringBuilder _buffer;
    private readonly TextWriter _innerWriter;
}