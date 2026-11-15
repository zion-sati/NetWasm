// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.Hosting.Execution;

/// <summary>Executes a built NetWasm artifact through its SDK-emitted local host contract.</summary>
public interface INetWasmArtifactClient
{
    ValueTask<NetWasmExecutionResult> ExecuteAsync(
        NetWasmArtifactExecutionRequest request,
        Stream standardOutput,
        Stream standardError,
        CancellationToken cancellationToken = default);
}
