namespace Serilog.Sinks.NamedPipe.Internals;

internal delegate void NamedPipeSinkEventHandler(NamedPipeSink sink);

internal delegate void NamedPipeSinkErrorEventHandler(NamedPipeSink sink, Exception ex);

internal delegate void NamedPipeSinkEventHandler<TEventArgs>(NamedPipeSink sink, TEventArgs e);

internal delegate void NamedPipeSinkErrorEventHandler<TEventArgs>(NamedPipeSink sink, TEventArgs e, Exception ex);
