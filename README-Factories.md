# Serilog.Sinks.NamedPipe.Factories

[![NuGet](https://img.shields.io/nuget/v/Serilog.Sinks.NamedPipe.Factories.svg)](https://www.nuget.org/packages/Serilog.Sinks.NamedPipe.Factories)
[![Build](https://github.com/lethek/Serilog.Sinks.NamedPipe/actions/workflows/dotnet.yml/badge.svg)](https://github.com/lethek/Serilog.Sinks.NamedPipe/actions/workflows/dotnet.yml)
[![License](https://img.shields.io/github/license/lethek/Serilog.Sinks.NamedPipe)](https://github.com/lethek/Serilog.Sinks.NamedPipe/blob/master/LICENSE)

Provides functionality for creating a factory that provides a pipe to read/write log events.

# Getting started

You probably want to use this package together with the [Serilog.Sinks.NamedPipe](https://www.nuget.org/packages/Serilog.Sinks.NamedPipe) package
or the [Serilog.Sinks.NamedPipe.Reader](https://www.nuget.org/packages/Serilog.Sinks.NamedPipe.Reader) package.
Both include this one as a transitive dependency so you shouldn't need to install this separately.

## Install the [package from NuGet](https://www.nuget.org/packages/Serilog.Sinks.NamedPipe.Factories):

```
dotnet add package Serilog.Sinks.NamedPipe.Factories
```

## Usage

This package provides a static NamedPipeFactories class with several methods for creating named-pipe factories:

| Method                                                | Description                                                                                                           |
|-------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------|
| `NamedPipeFactories.CreateFactory(...)`               | Creates a factory for custom configured pipes. You have complete control.                                             |
| `NamedPipeFactories.DefaultClientFactory(...)`        | Creates a factory for named-pipe clients using Byte transmission mode.                                                |
| `NamedPipeFactories.DefaultServerFactory(...)`        | Creates a factory for named-pipe servers using Byte transmission mode.                                                |
| `NamedPipeFactories.DefaultMessageClientFactory(...)` | Creates a factory for named-pipe clients using Message transmission mode.<br/>**This mode is only supported by Windows.** |
| `NamedPipeFactories.DefaultMessageServerFactory(...)` | Creates a factory for named-pipe servers using Message transmission mode.<br/>**This mode is only supported by Windows.** |

See the examples below for how to use these methods and their parameters.

## Examples

### CreateFactory

Allows manually creating a custom factory that provides a connected PipeStream:

```csharp
// This overload is useful if the getStream parameter can be synchronous. The connect parameter is always async.
var factory = NamedPipeFactories.CreateFactory(
    getStream: () => new NamedPipeClientStream(".", "myCustomPipe", PipeDirection.Out, PipeOptions.Asynchronous),
    connect: (pipe, cancellationToken) => pipe.ConnectAsync(cancellationToken)
);
```

```csharp
// This overload is useful if the getStream parameter needs to do some asynchronous work. The connect parameter is always async.
var factory = NamedPipeFactories.CreateFactory(
    getStream: async () => {
        await DoSomeWorkAsync();
        return new NamedPipeClientStream(".", "myCustomPipe", PipeDirection.Out, PipeOptions.Asynchronous);
    },
    connect: (pipe, cancellationToken) => pipe.ConnectAsync(cancellationToken)
);
```

The `getStream` function delegate parameter must create/provide an instance of PipeStream.

The `connect` function delegate parameter should connect the PipeStream to the other end of the pipe. It must not return
until the connection has opened or been cancelled. The `cancellationToken` parameter is provided by the sink and will be
used for cancelling connection attempts. This function will be called when a new pipe is needed, including whenever the
pipe connection is broken and needs to be reconnected.


### DefaultClientFactory

Creates a factory for named-pipe clients with some predefined defaults.
Any NamedPipeClientStream it creates will be assumed to be: local, Asynchronous, and will use Byte transmission mode.

```csharp
// This overload uses a pipe direction of PipeDirection.InOut by default
var factory = NamedPipeFactories.DefaultClientFactory("myDefaultByteStreamPipe");
```

```csharp
// This overload explicitly overrides the pipe direction
var factory = NamedPipeFactories.DefaultClientFactory("myDefaultByteStreamPipe", PipeDirection.Out);
```


### DefaultServerFactory

Creates a factory for named-pipe servers with some predefined defaults. Any NamedPipeServerStream it creates will be
Asynchronous and will use Byte transmission mode.

```csharp
// This overload uses a pipe direction of PipeDirection.InOut by default
var factory = NamedPipeFactories.DefaultServerFactory("myDefaultByteStreamPipe");
```

```csharp
// This overload explicitly overrides the pipe direction
var factory = NamedPipeFactories.DefaultServerFactory("myDefaultByteStreamPipe", PipeDirection.In);
```


### DefaultMessageClientFactory

Creates a factory for named-pipe clients with some predefined defaults.
Any NamedPipeClientStream it creates will be assumed to be: local, asynchronous, InOut direction, and use Message transmission-mode.

***Note:** Message transmission mode is only supported on Windows.*

```csharp
var factory = NamedPipeFactories.DefaultMessageClientFactory("myDefaultMessageStreamPipe");
```


### DefaultMessageServerFactory

Creates a factory for named-pipe servers with some predefined defaults.
Any NamedPipeServerStream it creates will be assumed to be: asynchronous, InOut direction, and use Message transmission-mode.

***Note:** Message transmission mode is only supported on Windows.*

```csharp
var factory = NamedPipeFactories.DefaultMessageServerFactory("myDefaultMessageStreamPipe");
```


## License

This library is licensed under the [MIT License](https://opensource.org/licenses/MIT).
