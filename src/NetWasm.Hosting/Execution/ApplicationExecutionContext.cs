// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using NetWasm.Hosting.Capabilities;

namespace NetWasm.Hosting.Execution;

internal sealed record ApplicationExecutionContext(
    ExecutionPlan Plan,
    NetWasmExecutionRequest Request,
    ImmutableArray<NetWasmPlatformImportProvider> PlatformProviders,
    ImmutableArray<NetWasmApplicationImportProvider> ApplicationProviders);
