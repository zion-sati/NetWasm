// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.Analysis;

internal readonly record struct ReachableMethodBatchObservation(
    int RequestCount,
    int WorkerCount,
    TimeSpan Elapsed,
    TimeSpan WorkerBusy,
    TimeSpan OrderedWait,
    int PeakBufferedResults);
