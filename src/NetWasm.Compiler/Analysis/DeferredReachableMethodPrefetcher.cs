// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Analysis;

/// <summary>
/// Leaves each method to be analyzed with its batch, for hosts that analyze on one thread.
/// </summary>
internal sealed class DeferredReachableMethodPrefetcher : IReachableMethodPrefetcher
{
    public void Prefetch(ReachableMethodRequest request) =>
        System.ArgumentNullException.ThrowIfNull(request);
}
