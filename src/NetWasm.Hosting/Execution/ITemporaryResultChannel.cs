using System;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.Hosting.Execution;

internal interface ITemporaryResultChannel : IAsyncDisposable
{
    string Path { get; }

    ValueTask<ReadOnlyMemory<byte>> ReadAsync(CancellationToken cancellationToken);
}

internal interface ITemporaryResultChannelFactory
{
    ITemporaryResultChannel Create();
}
