// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Analysis;

/// <summary>Starts analyzing a method the closure will ask for, ahead of its turn.</summary>
internal interface IReachableMethodPrefetcher
{
    void Prefetch(ReachableMethodRequest request);
}
