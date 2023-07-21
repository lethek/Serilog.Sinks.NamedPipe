using System.IO.Pipes;
using System.Threading.Channels;

using Hypothesist;

using Serilog.Events;
using Serilog.Formatting.Compact.Reader;
using Serilog.Parsing;
using Serilog.Sinks.NamedPipe.Internals;


namespace Serilog.Sinks.NamedPipe.Tests;

public class NamedPipeSinkTests
{
    [Fact]
    public async Task NamedPipeServer_WhenEmitting_ShouldWriteToNamedPipeCorrectly()
    {
        var pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeSink.CreateNamedPipeServerFactory(pipeName);
        using var sink = new NamedPipeSink(pipeFactory, null, null, 100);

        await using var client = new NamedPipeClientStream(pipeName);
        await client.ConnectAsync();
        using var reader = new StreamReader(client);
        Assert.True(client.IsConnected);

        sink.Emit(CreateEvent("Hello unit test"));

        Assert.Equal("Hello unit test", await RenderNextLogEventAsync(reader));
    }


    [Fact]
    public async Task NamedPipeClient_WhenEmitting_ShouldWriteToNamedPipeCorrectly()
    {
        var pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeSink.CreateNamedPipeClientFactory(pipeName);
        using var sink = new NamedPipeSink(pipeFactory, null, null, 100);

        await using var server = new NamedPipeServerStream(pipeName);
        await server.WaitForConnectionAsync();
        using var reader = new StreamReader(server);
        Assert.True(server.IsConnected);

        sink.Emit(CreateEvent("Hello unit test"));

        Assert.Equal("Hello unit test", await RenderNextLogEventAsync(reader));
    }


    [Fact]
    public async Task NamedPipeClient_WhenNamedPipeIsBroken_ShouldReconnect()
    {
        var pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeSink.CreateNamedPipeClientFactory(pipeName);
        using var sink = new NamedPipeSink(pipeFactory, null, null, 100);

        //Create an initial connection to read from the pipe, then break the pipe by allowing the connection to be disposed
        await using (var client = new NamedPipeServerStream(pipeName)) {
            await client.WaitForConnectionAsync();
            using var reader = new StreamReader(client);

            sink.Emit(CreateEvent("Message while connected"));

            Assert.Equal("Message while connected", await RenderNextLogEventAsync(reader));
        }


        //Logging while the pipe is broken allows the sink to detect and start reconnecting
        sink.Emit(CreateEvent("Detect broken pipe"));


        //Create a second connection to read from the pipe
        await using (var client = new NamedPipeServerStream(pipeName)) {
            await client.WaitForConnectionAsync();
            using var reader = new StreamReader(client);

            //Logs (such as the following) which were queued while the pipe was not connected, are finally delivered here upon reconnection
            Assert.Equal("Detect broken pipe", await RenderNextLogEventAsync(reader));

            sink.Emit(CreateEvent("Another message while connected"));

            Assert.Equal("Another message while connected", await RenderNextLogEventAsync(reader));
        }
    }


    [Fact]
    public async Task NamedPipeServer_WhenNamedPipeIsBroken_ShouldReconnect()
    {
        var pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeSink.CreateNamedPipeServerFactory(pipeName);
        using var sink = new NamedPipeSink(pipeFactory, null, null, 100);

        //Create an initial connection to read from the pipe, then break the pipe by allowing the connection to be disposed
        await using (var client = new NamedPipeClientStream(pipeName)) {
            await client.ConnectAsync();
            using var reader = new StreamReader(client);

            sink.Emit(CreateEvent("Message while connected"));

            Assert.Equal("Message while connected", await RenderNextLogEventAsync(reader));
        }


        //Logging while the pipe is broken allows the sink to detect and start reconnecting
        sink.Emit(CreateEvent("Detect broken pipe"));


        //Create a second connection to read from the pipe
        await using (var client = new NamedPipeClientStream(pipeName)) {
            await client.ConnectAsync();
            using var reader = new StreamReader(client);

            //Logs (such as the following) which were queued while the pipe was not connected, are finally delivered here upon reconnection
            Assert.Equal("Detect broken pipe", await RenderNextLogEventAsync(reader));

            sink.Emit(CreateEvent("Another message while connected"));

            Assert.Equal("Another message while connected", await RenderNextLogEventAsync(reader));
        }
    }


    [Fact]
    public async Task NamedPipeServer_WhenEmittingWhileDisconnected_ShouldQueueLogEventsUpToCapacity()
    {
        var pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeSink.CreateNamedPipeServerFactory(pipeName);
        using var sink = new NamedPipeSink(pipeFactory, null, null, 10);

        //Emit 20 log events while the pipe is disconnected
        for (var i = 0; i < 20; i++) {
            sink.Emit(CreateEvent(i.ToString()));
        }

        //We had set capacity to 10, so only the first 10 log events should be queued up; the rest should have been dropped
        Assert.Equal(10, sink.Channel.Reader.Count);

        //Subscribe for notifications of successful log writes and inform the hypothesis of remaining queue size
        var hypothesis = Hypothesis.For<int>();
        sink.OnWriteSuccess += (source, _) => hypothesis.Test(source.Channel.Reader.Count);

        //Allow all of those 10 queued log events to be delivered
        await using var client = new NamedPipeClientStream(pipeName);
        await client.ConnectAsync();
        using var reader = new StreamReader(client);
        for (var i = 0; i < 10; i++) {
            Assert.Equal(i.ToString(), await RenderNextLogEventAsync(reader));
        }

        //Validate the hypothesis that there are no more log events queued up
        await hypothesis.Any(queueSize => queueSize == 0).Validate(DefaultTimeout);
    }


    [Fact]
    public async Task NamedPipeClient_WhenEmittingWhileDisconnected_ShouldQueueLogEventsUpToCapacity()
    {
        var pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeSink.CreateNamedPipeClientFactory(pipeName);
        using var sink = new NamedPipeSink(pipeFactory, null, null, 10);

        //Emit 20 log events while the pipe is disconnected
        for (var i = 0; i < 20; i++) {
            sink.Emit(CreateEvent(i.ToString()));
        }

        //We had set capacity to 10, so only the first 10 log events should be queued up; the rest should have been dropped
        Assert.Equal(10, sink.Channel.Reader.Count);

        //Subscribe for notifications of successful log writes and inform the hypothesis of remaining queue size
        var hypothesis = Hypothesis.For<int>();
        sink.OnWriteSuccess += (source, _) => hypothesis.Test(source.Channel.Reader.Count);

        //Allow all of those 10 queued log events to be delivered
        await using var client = new NamedPipeServerStream(pipeName);
        await client.WaitForConnectionAsync();
        using var reader = new StreamReader(client);
        for (var i = 0; i < 10; i++) {
            Assert.Equal(i.ToString(), await RenderNextLogEventAsync(reader));
        }

        //Validate the hypothesis that there are no more log events queued up
        await hypothesis.Any(queueSize => queueSize == 0).Validate(DefaultTimeout);
    }


    [Fact]
    public async Task Dispose_ShouldCancelPumpAndCompleteReader()
    {
        ChannelReader<LogEvent> reader;
        Task worker;

        var hypothesis = Hypothesis.For<bool>();

        var pipeFactory = NamedPipeSink.CreateNamedPipeServerFactory(GeneratePipeName());
        using (var sink = new NamedPipeSink(pipeFactory, null, null, 100)) {
            sink.OnMessagePumpStopped += _ => hypothesis.Test(true);
            reader = sink.Channel.Reader;
            worker = sink.Worker;
        }

        //Validate whether the sink's message-pump worker signals that it has stopped, upon disposal
        await hypothesis.First(_ => true).Validate(DefaultTimeout);

        //The reader and worker tasks should now be completed
        Assert.True(reader.Completion.IsCompleted);
        Assert.True(worker.IsCompleted);
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


    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(1);
}
