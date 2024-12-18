using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading.Channels;

using Hypothesist;

using Serilog.Events;
using Serilog.Formatting.Compact.Reader;
using Serilog.Parsing;
using Serilog.Sinks.NamedPipe.Reader.Extensions;

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

        //Setup the sink and then dispose of it
        var pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeFactories.DefaultServerFactory(pipeName);
        await using (var sink = new NamedPipeSink(pipeFactory, null, null, 100)) {
            sink.OnMessagePumpStopped += _ => { stoppedSemaphore.Release(); };

            worker = sink.Worker;
            reader = sink.LogChannel.Reader;

            #if !NET6_0_OR_GREATER
            //NOTE: Shouldn't need the following NamedPipeClientStream code below, but it's the only way to reliably run
            //this unit-test and avoid an old .NET bug (https://github.com/dotnet/runtime/issues/40289) which can cause
            //the server to hang forever on Unix systems. Microsoft fixed it in .NET 6.0, however we still need to support
            //and test older versions of .NET.
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync();
            #endif
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
        //Setup the sink
        var pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeFactories.DefaultServerFactory(pipeName);
        await using var sink = new NamedPipeSink(pipeFactory, null, null, 100);

        //Setup the receiver
        await using var receiver = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous);
        await receiver.ConnectAsync();
        using var reader = new StreamReader(receiver);
        Assert.True(receiver.IsConnected);

        sink.Emit(CreateEvent("Hello unit test"));

        Assert.Equal("Hello unit test", await RenderNextLogEventAsync(reader));
    }


    [Fact]
    public async Task NamedPipeClient_WhenEmitting_ShouldWriteToNamedPipeCorrectly()
    {
        //Setup the sink
        var pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeFactories.DefaultClientFactory(pipeName);
        await using var sink = new NamedPipeSink(pipeFactory, null, null, 100);

        //Setup the receiver
        await using var receiver = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await receiver.WaitForConnectionAsync();
        using var reader = new StreamReader(receiver);
        Assert.True(receiver.IsConnected);

        sink.Emit(CreateEvent("Hello unit test"));

        Assert.Equal("Hello unit test", await RenderNextLogEventAsync(reader));
    }


    [Fact]
    public async Task NamedPipeClient_WhenNamedPipeIsBroken_ShouldReconnect()
    {
        using var pipeBrokenSemaphore = new SemaphoreSlim(0, 1);

        //Setup the sink
        var pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeFactories.DefaultClientFactory(pipeName);
        await using var sink = new NamedPipeSink(pipeFactory, null, null, 100);
        sink.OnPipeBroken += (sender, args) => { pipeBrokenSemaphore.Release(); };

        //Create an initial connection to read from the pipe, then break the pipe by allowing the connection to be disposed
        await using (var receiver = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous)) {
            await receiver.WaitForConnectionAsync();
            using var reader = new StreamReader(receiver);

            sink.Emit(CreateEvent("Message while connected"));

            Assert.Equal("Message while connected", await RenderNextLogEventAsync(reader));
        }


        //Logging while the pipe is broken allows the sink to detect and start reconnecting
        sink.Emit(CreateEvent("Detect broken pipe"));

        //Wait for the broken pipe to be detected before proceeding with trying to re-establish the connection
        await pipeBrokenSemaphore.WaitAsync(DefaultTimeout);


        //Create a second connection to read from the pipe
        await using (var receiver = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous)) {
            await receiver.WaitForConnectionAsync();
            using var reader = new StreamReader(receiver);

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

        //Setup the sink
        var pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeFactories.DefaultServerFactory(pipeName);
        await using var sink = new NamedPipeSink(pipeFactory, null, null, 100);
        sink.OnPipeBroken += (sender, args) => { pipeBrokenSemaphore.Release(); };

        //Create an initial connection to read from the pipe, then break the pipe by allowing the connection to be disposed
        await using (var receiver = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous)) {
            await receiver.ConnectAsync();
            using var reader = new StreamReader(receiver);

            sink.Emit(CreateEvent("Message while connected"));

            Assert.Equal("Message while connected", await RenderNextLogEventAsync(reader));
        }


        //Logging while the pipe is broken allows the sink to detect and start reconnecting
        sink.Emit(CreateEvent("Detect broken pipe"));

        //Wait for the broken pipe to be detected before proceeding with trying to re-establish the connection
        await pipeBrokenSemaphore.WaitAsync(DefaultTimeout);


        //Create a second connection to read from the pipe
        await using (var receiver = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous)) {
            await receiver.ConnectAsync();
            using var reader = new StreamReader(receiver);

            //Logs (such as the following) which were queued while the pipe was not connected, are finally delivered here upon reconnection
            Assert.Equal("Detect broken pipe", await RenderNextLogEventAsync(reader));

            sink.Emit(CreateEvent("Another message while connected"));

            Assert.Equal("Another message while connected", await RenderNextLogEventAsync(reader));
        }
    }


    [Fact]
    public async Task NamedPipeServer_WhenEmittingWhileDisconnected_ShouldQueueLogEventsUpToCapacity()
    {
        //Setup the sink
        var pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeFactories.DefaultServerFactory(pipeName);
        await using var sink = new NamedPipeSink(pipeFactory, null, null, 10);

        //Emit 20 log events while the pipe is disconnected
        for (var i = 0; i < 20; i++) {
            sink.Emit(CreateEvent(i.ToString()));
        }

        //We had set capacity to 10, so only the first 10 log events should be queued up; the rest should have been dropped
        Assert.Equal(10, sink.LogChannel.Reader.Count);

        //Subscribe for notifications of successful log writes and inform the hypothesis of remaining queue size
        var observer = new Observer<int>();
        var hypothesis = Hypothesis.On(observer).Timebox(DefaultTimeout);
        
        sink.OnWriteSuccess += (source, _) => observer.Add(source.LogChannel.Reader.Count);

        //Allow all of those 10 queued log events to be delivered
        await using var receiver = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await receiver.ConnectAsync();
        using var reader = new StreamReader(receiver);
        for (var i = 0; i < 10; i++) {
            Assert.Equal(i.ToString(), await RenderNextLogEventAsync(reader));
        }

        //Validate the hypothesis that there are no more log events queued up
        await hypothesis.Any().Match(queueSize => queueSize == 0).Validate();
    }


    [Fact]
    public async Task NamedPipeClient_WhenEmittingWhileDisconnected_ShouldQueueLogEventsUpToCapacity()
    {
        //Setup the sink
        var pipeName = GeneratePipeName();
        var pipeFactory = NamedPipeFactories.DefaultClientFactory(pipeName);
        await using var sink = new NamedPipeSink(pipeFactory, null, null, 10);

        //Emit 20 log events while the pipe is disconnected
        for (var i = 0; i < 20; i++) {
            sink.Emit(CreateEvent(i.ToString()));
        }

        //We had set capacity to 10, so only the first 10 log events should be queued up; the rest should have been dropped
        Assert.Equal(10, sink.LogChannel.Reader.Count);

        //Subscribe for notifications of successful log writes and inform the hypothesis of remaining queue size
        var observer = new Observer<int>();
        var hypothesis = Hypothesis.On(observer).Timebox(DefaultTimeout);
        sink.OnWriteSuccess += (source, _) => observer.Add(source.LogChannel.Reader.Count);

        //Allow all of those 10 queued log events to be delivered
        await using var receiver = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await receiver.WaitForConnectionAsync();
        using var reader = new StreamReader(receiver);
        for (var i = 0; i < 10; i++) {
            Assert.Equal(i.ToString(), await RenderNextLogEventAsync(reader));
        }

        //Validate the hypothesis that there are no more log events queued up
        await hypothesis.Any().Match(queueSize => queueSize == 0).Validate();
    }


#pragma warning disable CA1416
    [SkippableFact]
    public async Task NamedPipeServerWithCustomFactory_WhenEmittingMessages_ShouldWriteToNamedPipeCorrectly()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows));

        const string expectedMessageText = "Sink should write this log as a named-pipe Message rather than a stream of bytes";
        var pipeName = GeneratePipeName();

        //Custom factory which creates a named-pipe server stream and waits for a client to connect
        var pipeFactory = NamedPipeFactories.CreateFactory(
            //A named-pipe needs to be bi-directional (PipeDirection.InOut) to use Message transmision-mode 
            () => new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous),
            (server, cancellationToken) => server.WaitForConnectionAsync(cancellationToken)
        );
        await using var sink = new NamedPipeSink(pipeFactory, null, null, 100);

        //Create a client to connect to the sink's named-pipe server
        await using var receiver = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await receiver.ConnectAsync();
        //ReadMode must be manually set to PipeTransmissionMode.Message after connecting to allow reading Messages on the client side of the pipe
        receiver.ReadMode = PipeTransmissionMode.Message;

        sink.Emit(CreateEvent(expectedMessageText));

        //ReadMessageStringAsync is an extension method defined in the PipeStreamExtensions class of this project and will read an entire message (string)
        var message = await receiver.ReadMessageStringAsync();
        var logEvent = DeserializeClef(message).Single();
        var renderedMessageText = logEvent.RenderMessage();

        Assert.Equal(expectedMessageText, renderedMessageText);
    }
#pragma warning restore CA1416


#pragma warning disable CA1416
    [SkippableFact]
    public async Task NamedPipeClientWithCustomFactory_WhenEmittingMessages_ShouldWriteToNamedPipeCorrectly()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows));

        const string expectedMessageText = "Sink should write this log as a named-pipe Message rather than a stream of bytes";
        var pipeName = GeneratePipeName();

        //Custom factory which creates a named-pipe server stream and waits for a client to connect
        var pipeFactory = NamedPipeFactories.CreateFactory(
            //A named-pipe needs to be bi-directional (PipeDirection.InOut) to use Message transmision-mode 
            () => new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous),
            (client, cancellationToken) => client.ConnectAsync(cancellationToken)
        );
        await using var sink = new NamedPipeSink(pipeFactory, null, null, 100);

        //Create a server for the sink's named-pipe client to connect to
        await using var receiver = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
        await receiver.WaitForConnectionAsync();

        sink.Emit(CreateEvent(expectedMessageText));

        //ReadMessageStringAsync is an extension method defined in the PipeStreamExtensions class of this project and will read an entire message (string)
        var message = await receiver.ReadMessageStringAsync();
        var logEvent = DeserializeClef(message).Single();
        var renderedMessageText = logEvent.RenderMessage();

        Assert.Equal(expectedMessageText, renderedMessageText);
    }
#pragma warning restore CA1416


    private static async Task<string> RenderNextLogEventAsync(TextReader reader)
        => DeserializeClef(await reader.ReadLineAsync()).Single().RenderMessage();


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
