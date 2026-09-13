using System;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.Hosting.Execution;

internal interface IArtifactDocumentReader
{
    ValueTask<ReadOnlyMemory<byte>> ReadAsync(string path, CancellationToken cancellationToken);
}
