// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

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
    public ImmutableArray<string> RuntimeFeatures { get; init; } = [];
    public RuntimeNativeCallbackSupport? NativeCallbackSupport { get; init; }
    // Entry points of the C library functions the program imports (RuntimeSystemImportPartitioner).
    public ImmutableArray<string> SystemExports { get; init; } = [];
}

internal sealed record RuntimeLinkExport(string Name, byte Kind);

internal sealed record RuntimeLinkExportPlan(
    ImmutableArray<string> Arguments,
    ImmutableArray<RuntimeLinkExport> InternalExports);
