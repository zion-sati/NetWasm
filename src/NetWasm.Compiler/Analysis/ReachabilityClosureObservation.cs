// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.Analysis;

internal readonly record struct ReachabilityClosureObservation(
    int MethodRequestCount,
    TimeSpan MethodPublication,
    int DispatchCount,
    int ResolvedDispatchCount,
    TimeSpan DispatchPublication,
    TimeSpan Elapsed);
