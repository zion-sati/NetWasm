// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed record RuntimeLinkedMemoryLayout(
    string Target,
    long RuntimeGlobalBase,
    long DataEnd,
    long StackLow,
    long StackHigh,
    long HeapBase,
    long InitialMemorySizeBytes,
    long MaximumMemorySizeBytes);
