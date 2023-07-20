using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;

using Serilog.Events;
using Serilog.Formatting.Compact.Reader;
using Serilog.Parsing;


namespace Serilog.Sinks.NamedPipe.Tests;

public class NamedPipeSinkTests
{
    [Fact]
    public async Task NamedPipeServer_WhenEmitting_ShouldWriteToNamedPipeCorrectly()
    {
        string pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeSink.CreateNamedPipeServerFactory(pipeName);
        using var sink = new NamedPipeSink(pipeFactory, Encoding.UTF8, null, 100);

        await using var client = new NamedPipeClientStream(".", pipeName);
        await client.ConnectAsync();
        using var reader = new StreamReader(client, Encoding.UTF8);
        Assert.True(client.IsConnected);

        sink.Emit(CreateEvent("Hello unit test"));

        Assert.Equal("Hello unit test", await RenderNextLogEventAsync(reader));
    }


    [Fact]
    public async Task NamedPipeClient_WhenEmitting_ShouldWriteToNamedPipeCorrectly()
    {
        string pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeSink.CreateNamedPipeClientFactory(pipeName);
        using var sink = new NamedPipeSink(pipeFactory, Encoding.UTF8, null, 100);

        await using var server = new NamedPipeServerStream(pipeName);
        await server.WaitForConnectionAsync();
        using var reader = new StreamReader(server, Encoding.UTF8);
        Assert.True(server.IsConnected);

        sink.Emit(CreateEvent("Hello unit test"));

        Assert.Equal("Hello unit test", await RenderNextLogEventAsync(reader));
    }


    [Fact]
    public async Task NamedPipeServer_WhenNamedPipeIsBroken_ShouldReconnect()
    {
        string pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeSink.CreateNamedPipeServerFactory(pipeName);
        using var sink = new NamedPipeSink(pipeFactory, Encoding.UTF8, null, 100);

        //Create our first client connection to the pipe, then break the pipe by allowing the connection to be disposed
        await using (var client = new NamedPipeClientStream(pipeName)) {
            await client.ConnectAsync();
            using var reader = new StreamReader(client, Encoding.UTF8);

            sink.Emit(CreateEvent("Message while connected 1"));

            Assert.Equal("Message while connected 1", await RenderNextLogEventAsync(reader));
        }


        //Logging while the pipe is broken allows the sink to detect and start reconnecting
        sink.Emit(CreateEvent("Detect broken pipe"));


        //Create our second client connection to the pipe
        await using (var client = new NamedPipeClientStream(".", pipeName)) {
            await client.ConnectAsync();
            using var reader = new StreamReader(client, Encoding.UTF8);

            //Logs such as the following which were queued while the pipe was not connected, will be delivered here upon reconnection
            Assert.Equal("Detect broken pipe", await RenderNextLogEventAsync(reader));

            sink.Emit(CreateEvent("Message while connected 2"));

            Assert.Equal("Message while connected 2", await RenderNextLogEventAsync(reader));
        }
    }


    [Fact]
    public void Dispose_ShouldCancelPumpAndCompleteReader()
    {
        ChannelReader<LogEvent> reader;
        Task worker;

        var pipeFactory = NamedPipeSink.CreateNamedPipeServerFactory(GeneratePipeName());

        using (var sink = new NamedPipeSink(pipeFactory, null, null, 100)) {
            reader = sink.Channel.Reader;
            worker = sink.Worker;
        }

        //The reader should be completed when the sink is disposed
        Assert.True(Task.WaitAll(new[] { reader.Completion, worker }, timeout: TimeSpan.FromSeconds(1)));
    }


    private static async Task<string> RenderNextLogEventAsync(TextReader reader)
        => DeserializeClef(await reader.ReadLineAsync()).First().RenderMessage();


    private static IEnumerable<LogEvent> DeserializeClef(string? json)
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
