using System.IO.Pipes;

namespace Serilog.Sinks.NamedPipe;

/// <summary>
/// <para>
/// A factory method that for creating a <see cref="PipeStream"/> and opening its connection.
/// </para>
/// <para>The factory must not return until the connection has opened and is ready for writing/reading. It will be called
/// when a new pipe is needed, including whenever the pipe connection is broken and needs to be reconnected.
/// </para>
/// <para>
/// If you are implementing a custom PipeStreamFactory without using the NamedPipeFactories.CreateFactory methods, you
/// need to ensure that if an exception is thrown while the factory is waiting for a connection, the factory should
/// dispose of the <see cref="PipeStream"/> it created. In all other cases the factory must not dispose of it as the
/// sink will manage its lifetime.
/// </para>
/// </summary>
/// <param name="cancellationToken">A cancellation token that indicates when the sink is being disposed.
/// Upon cancellation, if the factory is waiting for a named pipe connection, it should cancel it.</param>
/// <returns>An instance of a <see cref="PipeStream"/> implementation. E.g.
/// <see cref="NamedPipeClientStream"/>, <see cref="NamedPipeServerStream"/>,
/// <see cref="AnonymousPipeClientStream"/>, <see cref="AnonymousPipeServerStream"/>.</returns>
public delegate ValueTask<PipeStream> PipeStreamFactory(CancellationToken cancellationToken);
