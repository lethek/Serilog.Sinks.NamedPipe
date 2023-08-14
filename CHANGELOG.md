# Change Log

---

## 2.0.1

### Bug Fixes

* Fixed a Serilog.Sinks.NamedPipe.Reader bug where reading logs in Message transmission mode returned an empty string when the pipe's connection breaks.

---

## 2.0.0

### Breaking Changes

* Default pipe direction (when none specified) is now `PipeDirection.InOut` for all factories and sinks.
* Moved factories into the `NamedPipeFactories` class from the new [Serilog.Sinks.NamedPipe.Factories](https://www.nuget.org/packages/Serilog.Sinks.NamedPipe.Factories) package.
  * `NamedPipeSink.CreatePipeStreamFactory<T>` is now `NamedPipeFactories.CreateFactory<T>`.
  * `NamedPipeSink.DefaultClientPipeStreamFactory` is now `NamedPipeFactories.DefaultClientFactory`.
  * `NamedPipeSink.DefaultServerPipeStreamFactory` is now `NamedPipeFactories.DefaultServerFactory`.

### Features

* Added new project for reading log events from a named pipe (particularly one written to by a NamedPipeSink): [Serilog.Sinks.NamedPipe.Reader](https://www.nuget.org/packages/Serilog.Sinks.NamedPipe.Reader).
* Added new project for creating a factory that provides a pipe to read/write log events: [Serilog.Sinks.NamedPipe.Factories](https://www.nuget.org/packages/Serilog.Sinks.NamedPipe.Factories).
* Added additional factories for creating named-pipe factories that use Message transmission mode:
  * `NamedPipeFactories.DefaultMessageClientFactory`
  * `NamedPipeFactories.DefaultMessageServerFactory`
* Added a `pipeTransmissionMode` parameter to the Serilog configuration extension method `NamedPipeClient(...)`

---

## 1.1.0

### Features

* NamedPipeSink is now public so notification events may be subscribed to and custom factory methods called.
* Exposed public static methods for creating PipeStreamFactory instances:
  * `NamedPipeSink.CreatePipeStreamFactory<T>`
  * `NamedPipeSink.DefaultClientPipeStreamFactory`
  * `NamedPipeSink.DefaultServerPipeStreamFactory` 

---

## 1.0.1

### Features

* Specify Serilog v2.8.0 as a minimum dependency rather than v3.0.
* Implemented IAsyncDisposable in NamedPipeSink.  

---

## 1.0.0

### Features
 
* First version
