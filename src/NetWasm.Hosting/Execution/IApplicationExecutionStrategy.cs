using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.Hosting.Execution;

internal interface IApplicationExecutionStrategy
{
    ValueTask<NetWasmExecutionResult> ExecuteAsync(
        ApplicationExecutionContext context,
        CancellationToken cancellationToken);
}
