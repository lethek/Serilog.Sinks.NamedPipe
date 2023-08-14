# Serilog.Sinks.NamedPipe

[![NuGet](https://img.shields.io/nuget/v/Serilog.Sinks.NamedPipe.svg)](https://www.nuget.org/packages/Serilog.Sinks.NamedPipe)
[![Build](https://github.com/lethek/Serilog.Sinks.NamedPipe/actions/workflows/dotnet.yml/badge.svg)](https://github.com/lethek/Serilog.Sinks.NamedPipe/actions/workflows/dotnet.yml)
[![License](https://img.shields.io/github/license/lethek/Serilog.Sinks.NamedPipe)](https://github.com/lethek/Serilog.Sinks.NamedPipe/blob/master/LICENSE)

This repository hosts the projects for three separate NuGet packages:

- [Serilog.Sinks.NamedPipe](https://www.nuget.org/packages/Serilog.Sinks.NamedPipe)

  Provides a Serilog Sink for writing log events to a named pipe.

  Read more about it in [README-Sink.md](README-Sink.md).


- [Serilog.Sinks.NamedPipe.Reader](https://www.nuget.org/packages/Serilog.Sinks.NamedPipe.Reader)

  Provides functionality for reading Serilog log events asynchronously from a named pipe.

  Read more about it in [README-Reader.md](README-Reader.md).


- [Serilog.Sinks.NamedPipe.Factories](https://www.nuget.org/packages/Serilog.Sinks.NamedPipe.Factories)

  Provides functionality for creating a factory that provides a pipe to read/write log events.
  This package is used by the other two packages so you'll get it as a transitive dependency if you install either of the other two packages.  

  Read more about it in [README-Factories.md](README-Factories.md).

## License

These libraries are licensed under the [MIT License](https://opensource.org/licenses/MIT).
