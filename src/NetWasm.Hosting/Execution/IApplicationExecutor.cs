using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.Hosting.Execution;

internal interface IApplicationExecutor
{
    ValueTask<NetWasmExecutionResult> ExecuteAsync(
        ApplicationExecutionRequest request,
        CancellationToken cancellationToken);
}
