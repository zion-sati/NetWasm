// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.Hosting.Execution;

internal sealed class FileArtifactDocumentReader : IArtifactDocumentReader
{
    public async ValueTask<ReadOnlyMemory<byte>> ReadAsync(
        string path,
        CancellationToken cancellationToken) =>
        await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
}
