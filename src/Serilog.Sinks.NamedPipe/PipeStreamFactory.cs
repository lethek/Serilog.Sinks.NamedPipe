using System.IO.Pipes;

namespace Serilog.Sinks.NamedPipe;

public delegate Task<PipeStream> PipeStreamFactory(CancellationToken cancellationToken);
