using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeLinkExportPlanBuilder
{
    RuntimeLinkExportPlan Build(RuntimeLinkExportPlanRequest request);
}

internal sealed record RuntimeLinkExportPlanRequest(
    ImmutableArray<string> PublicExports,
    ImmutableArray<RuntimeNativeBinding> NativeBindings)
{
    public RuntimeNativeCallbackSupport? NativeCallbackSupport { get; init; }
}

internal sealed record RuntimeLinkExport(string Name, byte Kind);

internal sealed record RuntimeLinkExportPlan(
    ImmutableArray<string> Arguments,
    ImmutableArray<RuntimeLinkExport> InternalExports);
