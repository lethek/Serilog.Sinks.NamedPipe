namespace Serilog.Sinks.NamedPipe.Internals;

internal delegate void NamedPipeSinkEventHandler(NamedPipeSink sink);

internal delegate void NamedPipeSinkEventHandler<TEventArgs>(NamedPipeSink sink, TEventArgs e);
