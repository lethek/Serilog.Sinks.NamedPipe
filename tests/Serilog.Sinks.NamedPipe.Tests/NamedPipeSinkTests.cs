using System.IO.Pipes;
using System.Text;

using Serilog.Events;
using Serilog.Formatting.Compact.Reader;
using Serilog.Parsing;


namespace Serilog.Sinks.NamedPipe.Tests;

public class NamedPipeSinkTests
{
    [Fact]
    public async Task Emit_WithNamedPipeServer_WritesToNamedPipeCorrectly()
    {
        string pipeName = GeneratePipeName();
        var factory = NamedPipeSink.CreateNamedPipeServerFactory(pipeName);
        using var sink = new NamedPipeSink(factory, Encoding.UTF8, null, 10000);

        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous);
        await client.ConnectAsync();
        using var reader = new StreamReader(client, Encoding.UTF8);
        Assert.True(client.IsConnected);

        sink.Emit(CreateEvent("Hello unit test"));

        var logJson = await reader.ReadLineAsync();
        var logEvent = DeserializeCompactLogEvents(logJson).First();
        Assert.Equal("Hello unit test", logEvent.MessageTemplate.Text);
    }


    [Fact]
    public async Task Emit_WithNamedPipeClient_WritesToNamedPipeCorrectly()
    {
        string pipeName = GeneratePipeName();
        var factory = NamedPipeSink.CreateNamedPipeClientFactory(pipeName);
        using var sink = new NamedPipeSink(factory, Encoding.UTF8, null, 10000);

        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await server.WaitForConnectionAsync();
        using var reader = new StreamReader(server, Encoding.UTF8);
        Assert.True(server.IsConnected);

        sink.Emit(CreateEvent("Hello unit test"));

        var logJson = await reader.ReadLineAsync();
        var logEvent = DeserializeCompactLogEvents(logJson).First();
        Assert.Equal("Hello unit test", logEvent.MessageTemplate.Text);
    }

    
    private static IEnumerable<LogEvent> DeserializeCompactLogEvents(string? json)
    {
        using var txtReader = new StringReader(json ?? "");
        var logReader = new LogEventReader(txtReader);
        if (logReader.TryRead(out var logEvent)) {
            yield return logEvent;
        }
    }


    private static LogEvent CreateEvent(string text)
        => new(
            DateTimeOffset.MaxValue,
            LogEventLevel.Error,
            null,
            new MessageTemplate(text, Enumerable.Empty<MessageTemplateToken>()),
            Enumerable.Empty<LogEventProperty>()
        );


    private static string GeneratePipeName()
        => @$"Serilog.Sinks.NamedPipe.Tests\{Guid.NewGuid()}";
}

public class SimplifiedLogEvent
{
    public DateTimeOffset Timestamp { get; set; }

    public string MessageTemplate { get; set; }

    public LogEventLevel Level { get; set; }

    public Exception Exception { get; set; }

    public string RenderedMessage { get; set; }

    public IDictionary<string, object> Properties { get; set; }
}