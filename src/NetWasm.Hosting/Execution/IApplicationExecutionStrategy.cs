// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.Hosting.Execution;

internal interface IApplicationExecutionStrategy
{
    ValueTask<NetWasmExecutionResult> ExecuteAsync(
        ApplicationExecutionContext context,
        CancellationToken cancellationToken);
}
