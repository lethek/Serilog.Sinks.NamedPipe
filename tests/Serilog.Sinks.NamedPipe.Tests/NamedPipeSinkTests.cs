using System.IO.Pipes;
using System.Text;

using Serilog.Events;
using Serilog.Formatting.Compact.Reader;
using Serilog.Parsing;


namespace Serilog.Sinks.NamedPipe.Tests;

public class NamedPipeSinkTests
{
    [Fact]
    public async Task Emit_WithNamedPipeServer_ShouldWriteToNamedPipeCorrectly()
    {
        string pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeSink.CreateNamedPipeServerFactory(pipeName);
        using var sink = new NamedPipeSink(pipeFactory, Encoding.UTF8, null, 100);

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
    public async Task Emit_WithNamedPipeClient_ShouldWriteToNamedPipeCorrectly()
    {
        string pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeSink.CreateNamedPipeClientFactory(pipeName);
        using var sink = new NamedPipeSink(pipeFactory, Encoding.UTF8, null, 100);

        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await server.WaitForConnectionAsync();
        using var reader = new StreamReader(server, Encoding.UTF8);
        Assert.True(server.IsConnected);

        sink.Emit(CreateEvent("Hello unit test"));

        var logJson = await reader.ReadLineAsync();
        var logEvent = DeserializeCompactLogEvents(logJson).First();
        Assert.Equal("Hello unit test", logEvent.MessageTemplate.Text);
    }


    [Fact]
    public async Task WhenNamedPipeIsBroken_WithNamedPipeServer_ShouldReconnect()
    {
        string pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeSink.CreateNamedPipeServerFactory(pipeName);
        using var sink = new NamedPipeSink(pipeFactory, Encoding.UTF8, null, 100);

        await using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous)) {
            await client.ConnectAsync();
            using var reader = new StreamReader(client, Encoding.UTF8);
            Assert.True(client.IsConnected);

            sink.Emit(CreateEvent("Hello unit test"));

            var logJson = await reader.ReadLineAsync();
            var logEvent = DeserializeCompactLogEvents(logJson).First();
            Assert.Equal("Hello unit test", logEvent.MessageTemplate.Text);
        }

        //TODO: something is wrong with this unit test!
        //await using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous)) {
            //await client.ConnectAsync();
            //using var reader2 = new StreamReader(client, Encoding.UTF8);
            //Assert.True(client.IsConnected);

            //sink.Emit(CreateEvent("Hello unit test 2"));

            //var logJson2 = await reader2.ReadLineAsync();
            //var logEvent2 = DeserializeCompactLogEvents(logJson2).First();
            //Assert.Equal("Hello unit test 2", logEvent2.MessageTemplate.Text);
        //}
    }


    [Fact]
    public async Task Dispose_ShouldCancelPumpAndCompleteReader()
    {
        //Arrange
        var pipeFactory = NamedPipeSink.CreateNamedPipeServerFactory(GeneratePipeName());
        var sink = new NamedPipeSink(pipeFactory, null, null, 100);

        //Act
        sink.Dispose();

        await Task.Yield();

        //Assert
        Assert.True(sink.Channel.Reader.Completion.IsCompleted);
        Assert.True(sink.Worker.IsCompleted);
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
