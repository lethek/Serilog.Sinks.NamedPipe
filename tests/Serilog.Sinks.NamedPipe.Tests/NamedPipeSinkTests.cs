using System.IO.Pipes;
using System.Threading.Channels;

using Hypothesist;

using Serilog.Events;
using Serilog.Formatting.Compact.Reader;
using Serilog.Parsing;
using Serilog.Sinks.NamedPipe.Internals;

using Xunit.Abstractions;


namespace Serilog.Sinks.NamedPipe.Tests;

public class NamedPipeSinkTests
{
    public NamedPipeSinkTests(ITestOutputHelper output)
    {
        Output = output;
        Debugging.SelfLog.Enable(Output.WriteLine);
    }


    [Fact]
    public async Task Dispose_ShouldCancelPumpAndCompleteReader()
    {
        Task worker;
        ChannelReader<LogEvent> reader;

        using var stoppedSemaphore = new SemaphoreSlim(0, 1);

        var pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeSink.NamedPipeServerConnectionFactory(pipeName);
        using (var sink = new NamedPipeSink(pipeFactory, null, null, 100)) {
            sink.OnMessagePumpStopped += _ => { stoppedSemaphore.Release(); };

            worker = sink.Worker;
            reader = sink.Channel.Reader;

            //NOTE: Shouldn't need the following NamedPipeClientStream code below, but it's the only way to reliably run
            //this unit-test and avoid an old .NET bug (https://github.com/dotnet/runtime/issues/40289) which can cause
            //the server to hang forever on Unix systems. Microsoft fixed it in .NET 6.0.
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync();
        }

        //Wait until the message pump has stopped
        await stoppedSemaphore.WaitAsync(DefaultTimeout);

        //The reader and worker tasks should now be completed
        Assert.True(worker.IsCompleted, "Worker task should be completed");
        Assert.True(reader.Completion.IsCompleted, "Channel Reader should be completed");
    }


    [Fact]
    public async Task NamedPipeServer_WhenEmitting_ShouldWriteToNamedPipeCorrectly()
    {
        var pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeSink.NamedPipeServerConnectionFactory(pipeName);
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
        var pipeFactory = NamedPipeSink.NamedPipeClientConnectionFactory(pipeName);
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
        using var pipeBrokenSemaphore = new SemaphoreSlim(0, 1);

        var pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeSink.NamedPipeClientConnectionFactory(pipeName);
        using var sink = new NamedPipeSink(pipeFactory, null, null, 100);
        sink.OnPipeBroken += (sender, args) => { pipeBrokenSemaphore.Release(); };

        //Create an initial connection to read from the pipe, then break the pipe by allowing the connection to be disposed
        await using (var client = new NamedPipeServerStream(pipeName)) {
            await client.WaitForConnectionAsync();
            using var reader = new StreamReader(client);

            sink.Emit(CreateEvent("Message while connected"));

            Assert.Equal("Message while connected", await RenderNextLogEventAsync(reader));
        }


        //Logging while the pipe is broken allows the sink to detect and start reconnecting
        sink.Emit(CreateEvent("Detect broken pipe"));

        //Wait for the broken pipe to be detected before proceeding with trying to re-establish the connection
        await pipeBrokenSemaphore.WaitAsync(DefaultTimeout);


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
        using var pipeBrokenSemaphore = new SemaphoreSlim(0, 1);

        var pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeSink.NamedPipeServerConnectionFactory(pipeName);
        using var sink = new NamedPipeSink(pipeFactory, null, null, 100);
        sink.OnPipeBroken += (sender, args) => { pipeBrokenSemaphore.Release(); };

        //Create an initial connection to read from the pipe, then break the pipe by allowing the connection to be disposed
        await using (var client = new NamedPipeClientStream(pipeName)) {
            await client.ConnectAsync();
            using var reader = new StreamReader(client);

            sink.Emit(CreateEvent("Message while connected"));

            Assert.Equal("Message while connected", await RenderNextLogEventAsync(reader));
        }


        //Logging while the pipe is broken allows the sink to detect and start reconnecting
        sink.Emit(CreateEvent("Detect broken pipe"));

        //Wait for the broken pipe to be detected before proceeding with trying to re-establish the connection
        await pipeBrokenSemaphore.WaitAsync(DefaultTimeout);


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
        var pipeFactory = NamedPipeSink.NamedPipeServerConnectionFactory(pipeName);
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
        var pipeFactory = NamedPipeSink.NamedPipeClientConnectionFactory(pipeName);
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


    private ITestOutputHelper Output { get; set; }


    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);
}
