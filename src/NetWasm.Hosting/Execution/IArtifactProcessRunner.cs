using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.Hosting.Execution;

internal interface IArtifactProcessRunner
{
    ValueTask<int> RunAsync(
        ArtifactProcessStart start,
        Stream standardOutput,
        Stream standardError,
        CancellationToken cancellationToken);
}
