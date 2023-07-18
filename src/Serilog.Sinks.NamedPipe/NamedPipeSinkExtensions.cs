using System.Text;

using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Sinks.NamedPipe;


// ReSharper disable once CheckNamespace
namespace Serilog;

public static class NamedPipeLoggerExtensions
{
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
