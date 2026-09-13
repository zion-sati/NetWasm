using System.Collections.Immutable;
using NetWasm.Hosting.Capabilities;

namespace NetWasm.Hosting.Execution;

internal sealed record ApplicationExecutionContext(
    ExecutionPlan Plan,
    NetWasmExecutionRequest Request,
    ImmutableArray<NetWasmPlatformImportProvider> PlatformProviders,
    ImmutableArray<NetWasmApplicationImportProvider> ApplicationProviders);
