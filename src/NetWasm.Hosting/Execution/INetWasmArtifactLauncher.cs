using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.Hosting.Execution;

internal interface INetWasmArtifactLauncher
{
    ValueTask<NetWasmExecutionResult> ExecuteAsync(
        ExecutionDescriptor descriptor,
        NetWasmArtifactPaths paths,
        ImmutableArray<string> arguments,
        Stream standardOutput,
        Stream standardError,
        CancellationToken cancellationToken);
}
