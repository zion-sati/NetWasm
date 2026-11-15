// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

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
