# Serilog.Sinks.NamedPipe.Reader

[![NuGet](https://img.shields.io/nuget/v/Serilog.Sinks.NamedPipe.Reader.svg)](https://www.nuget.org/packages/Serilog.Sinks.NamedPipe.Reader)
[![Build](https://github.com/lethek/Serilog.Sinks.NamedPipe/actions/workflows/dotnet.yml/badge.svg)](https://github.com/lethek/Serilog.Sinks.NamedPipe/actions/workflows/dotnet.yml)
[![License](https://img.shields.io/github/license/lethek/Serilog.Sinks.NamedPipe)](https://github.com/lethek/Serilog.Sinks.NamedPipe/blob/master/LICENSE)

Provides functionality for reading Serilog log events asynchronously from a named pipe.

It's expected that these events would be written by the [Serilog.Sinks.NamedPipe](https://www.nuget.org/packages/Serilog.Sinks.NamedPipe) Serilog sink but that doesn't necessarily need to be the case.

# Getting started

## Install the [package from NuGet](https://www.nuget.org/packages/Serilog.Sinks.NamedPipe.Reader):

```
dotnet add package Serilog.Sinks.NamedPipe.Reader
```

## Usage

```csharp
// First you need a factory that provides a pipe to read from
var getPipeConnection = NamedPipeFactories.DefaultClientFactory("pipeName");

// Then create a NamedPipeAsyncReader for reading log events from the pipe
var reader = new NamedPipeAsyncReader(getPipeConnection);

// Asynchronously read log events from the pipe. It will automatically try to reconnect if the pipe is broken, until you cancel the cancellationToken.
await foreach (var msg in reader.ReadAllLogEventsAsync(cancellationToken)) {
    Console.WriteLine($"Received log: {msg.RenderMessage()}");
}
```

## License

This library is licensed under the [MIT License](https://opensource.org/licenses/MIT).
