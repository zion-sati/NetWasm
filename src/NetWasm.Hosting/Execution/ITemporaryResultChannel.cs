// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.Hosting.Execution;

internal interface ITemporaryResultChannel : IAsyncDisposable
{
    string Path { get; }

    ValueTask<ReadOnlyMemory<byte>> ReadAsync(CancellationToken cancellationToken);
}

internal interface ITemporaryResultChannelFactory
{
    ITemporaryResultChannel Create();
}
