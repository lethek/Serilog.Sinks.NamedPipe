# Serilog.Sinks.NamedPipe

[![Build & Publish](https://github.com/lethek/Serilog.Sinks.NamedPipe/actions/workflows/dotnet.yml/badge.svg)](https://github.com/lethek/Serilog.Sinks.NamedPipe/actions/workflows/dotnet.yml)
[![NuGet](https://img.shields.io/nuget/v/Serilog.Sinks.NamedPipe.svg)](https://www.nuget.org/packages/Serilog.Sinks.NamedPipe)
[![GitHub license](https://img.shields.io/github/license/lethek/Serilog.Sinks.NamedPipe)](https://github.com/lethek/Serilog.Sinks.NamedPipe/blob/master/LICENSE)

This Serilog sink writes events to a named pipe.

It supports automatically re-establishing a connection to the named pipe if it closes/fails, and also buffering undelivered log events.

The sink uses a background worker for sending events to the named pipe, and will not block the calling thread.

# Getting started

## Install the [package from Nuget](https://www.nuget.org/packages/Serilog.Sinks.NamedPipe):

```
dotnet add package Serilog.Sinks.NamedPipe
```

## Configure the logger:

There are three different named pipe sink implementations you can choose from. See the following examples at their most basic (note however there are several additional optional parameters available and documented further down):

### 1. Host a NamedPipeServerStream in the sink

This sink will create a NamedPipeServerStream and wait for a connection from a named pipe client.

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.NamedPipeServer("pipeName")
    .CreateLogger();
```

### 2. Host a NamedPipeClientStream in the sink

This sink will create a NamedPipeClientStream and connect to a named pipe server.

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.NamedPipeClient("pipeName")
    .CreateLogger();
```

### 3. Host any kind of PipeStream in the sink

This sink allows you complete control over the creation of the named pipe stream.

The factory is called each time a new connection is required and must also not return until it has a connected stream.

The factory should only dispose of the stream if an exception is thrown while connecting. Under all other conditions,
the sink will dispose of the stream when it is finished with it.

```csharp
var factory = NamedPipeFactories.CreateFactory(
    getStream: () => new NamedPipeServerStream("pipeName", PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous),
    connect: (pipe, cancellationToken) => pipe.WaitForConnectionAsync(cancellationToken)
);

Log.Logger = new LoggerConfiguration()
    .WriteTo.NamedPipe(factory)
    .CreateLogger();
```

Or more verbosely, foregoing the convenience of `NamedPipeFactories.CreateFactory(getStream, connect)`:

```csharp
async ValueTask<PipeStream> MyPipeStreamFactory(CancellationToken cancellationToken) {
    //This example uses a NamedPipeServerStream in Message transmission mode, but you can use any kind of PipeStream.
    var pipe = new NamedPipeServerStream("pipeName", PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
    try {
        //Wait for a client to connect.
        await pipe.WaitForConnectionAsync(cancellationToken);
        return pipe;
    } catch {
        //Dispose of the stream if it errors while connecting, then propogate the exception out so the sink can handle it.
        pipe.Dispose();
        throw;
    }
}

Log.Logger = new LoggerConfiguration()
    .WriteTo.NamedPipe(MyPipeStreamFactory)
    .CreateLogger();
```

## Sink Options

All sinks also provide the following optional parameters:

| Name                     | Default               | Description |
| ------------------------ | --------------------- | ----------- |
| pipeDirection            | PipeDirection.Out     | The direction of the pipe from the sink's perspective. |
| encoding                 | UTF-8 without BOM     | Character encoding used to write to the named pipe. |
| formatter                | CompactJsonFormatter  | A formatter, such as JsonFormatter, to convert the log events into text for the named pipe. |
| restrictedToMinimumLevel | LogEventLevel.Verbose | The minimum level for events passed through the sink. Ignored when levelSwitch is specified. |
| levelSwitch              | null                  | A switch allowing the pass-through minimum level to be changed at runtime. |
| bufferSize               | 10000                 | The size of the concurrent queue used to feed the background worker thread. If the worker is unable to write events to the named pipe quickly enough and the queue is filled, subsequent events will be dropped until room is made in the queue. Set this to 0 for an unbounded queue. |

