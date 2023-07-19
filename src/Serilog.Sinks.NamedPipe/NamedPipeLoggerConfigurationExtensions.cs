using System.IO.Pipes;
using System.Text;

using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;
using Serilog.Formatting.Json;
using Serilog.Sinks.NamedPipe;


// ReSharper disable once CheckNamespace
namespace Serilog;

public static class NamedPipeLoggerConfigurationExtensions
{
    /// <summary>
    /// Write log events to the specified named pipe, using an automatically created pipe client.
    /// </summary>
    /// <param name="sinkConfiguration">Logger sink configuration.</param>
    /// <param name="pipeName">Name of the pipe (assumed to be on the local computer). A pipe client with this name is created.</param>
    /// <param name="encoding">Character encoding used to write to the named pipe. The default is UTF-8 without BOM.</param>
    /// <param name="formatter">A formatter, such as <see cref="JsonFormatter"/>, to convert the log events into text for the
    /// named pipe. The default is <see cref="CompactJsonFormatter"/>.</param>
    /// <param name="restrictedToMinimumLevel">The minimum level for events passed through the sink. Ignored when
    /// <paramref name="levelSwitch"/> is specified.</param>
    /// <param name="levelSwitch">A switch allowing the pass-through minimum level to be changed at runtime.</param>
    /// <param name="bufferSize">The size of the concurrent queue used to feed the background worker thread. If the worker is
    /// unable to write events to the named pipe quickly enough and the queue is filled, subsequent events will be dropped
    /// until room is made in the queue. The default is 10000. Set this to 0 for an unbounded queue.</param>
    /// <returns>A <see cref="LoggerConfiguration"/> allowing configuration to continue.</returns>
    public static LoggerConfiguration NamedPipeClient(
        this LoggerSinkConfiguration sinkConfiguration,
        string pipeName,
        Encoding? encoding = null,
        ITextFormatter? formatter = null,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose,
        LoggingLevelSwitch? levelSwitch = null,
        int bufferSize = DefaultBufferCapacity
    )
    {
        var pipeFactory = NamedPipeSink.CreateNamedPipeClientFactory(pipeName);
        return NamedPipe(sinkConfiguration, pipeFactory, encoding, formatter, restrictedToMinimumLevel, levelSwitch, bufferSize);
    }


    /// <summary>
    /// Write log events to the specified named pipe, using an automatically created pipe server.
    /// </summary>
    /// <param name="sinkConfiguration">Logger sink configuration.</param>
    /// <param name="pipeName">Name of the pipe. A pipe server with this name is created.</param>
    /// <param name="encoding">Character encoding used to write to the named pipe. The default is UTF-8 without BOM.</param>
    /// <param name="formatter">A formatter, such as <see cref="JsonFormatter"/>, to convert the log events into text for the
    /// named pipe. The default is <see cref="CompactJsonFormatter"/>.</param>
    /// <param name="restrictedToMinimumLevel">The minimum level for events passed through the sink. Ignored when
    /// <paramref name="levelSwitch"/> is specified.</param>
    /// <param name="levelSwitch">A switch allowing the pass-through minimum level to be changed at runtime.</param>
    /// <param name="bufferSize">The size of the concurrent queue used to feed the background worker thread. If the worker is
    /// unable to write events to the named pipe quickly enough and the queue is filled, subsequent events will be dropped
    /// until room is made in the queue. The default is 10000. Set this to 0 for an unbounded queue.</param>
    /// <returns>A <see cref="LoggerConfiguration"/> allowing configuration to continue.</returns>
    public static LoggerConfiguration NamedPipeServer(
        this LoggerSinkConfiguration sinkConfiguration,
        string pipeName,
        Encoding? encoding = null,
        ITextFormatter? formatter = null,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose,
        LoggingLevelSwitch? levelSwitch = null,
        int bufferSize = DefaultBufferCapacity
    )
    {
        var pipeFactory = NamedPipeSink.CreateNamedPipeServerFactory(pipeName);
        return NamedPipe(sinkConfiguration, pipeFactory, encoding, formatter, restrictedToMinimumLevel, levelSwitch, bufferSize);
    }


    /// <summary>
    /// Write log events to a named pipe, using a factory that allows you to control pipe creation.
    /// </summary>
    /// <param name="sinkConfiguration">Logger sink configuration.</param>
    /// <param name="pipeStreamFactory">A factory that will be called to create a <see cref="PipeStream"/> and open its
    /// connection. The factory must not return until the connection has opened and is ready for writing. It will be called
    /// when a new pipe is needed, including whenever the pipe connection is broken and needs to be reconnected.</param>
    /// <param name="encoding">Character encoding used to write to the named pipe. The default is UTF-8 without BOM.</param>
    /// <param name="formatter">A formatter, such as <see cref="JsonFormatter"/>, to convert the log events into text for the
    /// named pipe. The default is <see cref="CompactJsonFormatter"/>.</param>
    /// <param name="restrictedToMinimumLevel">The minimum level for events passed through the sink. Ignored when
    /// <paramref name="levelSwitch"/> is specified.</param>
    /// <param name="levelSwitch">A switch allowing the pass-through minimum level to be changed at runtime.</param>
    /// <param name="bufferSize">The size of the concurrent queue used to feed the background worker thread. If the worker is
    /// unable to write events to the named pipe quickly enough and the queue is filled, subsequent events will be dropped
    /// until room is made in the queue. The default is 10000. Set this to 0 for an unbounded queue.</param>
    /// <returns>A <see cref="LoggerConfiguration"/> allowing configuration to continue.</returns>
    public static LoggerConfiguration NamedPipe(
        LoggerSinkConfiguration sinkConfiguration,
        PipeStreamFactory pipeStreamFactory,
        Encoding? encoding = null,
        ITextFormatter? formatter = null,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose,
        LoggingLevelSwitch? levelSwitch = null,
        int bufferSize = DefaultBufferCapacity
    )
    {
        ArgumentNullException.ThrowIfNull(sinkConfiguration);
        return sinkConfiguration.Sink(new NamedPipeSink(pipeStreamFactory, encoding, formatter, bufferSize), restrictedToMinimumLevel, levelSwitch);
    }


    private const int DefaultBufferCapacity = 10000;
}
