// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;

namespace NetWasm.Compiler.Core;

public interface IIndexedWorkExecutor
{
    TResult[] Execute<TWorker, TResult>(
        IReadOnlyList<TWorker> workers,
        int requestCount,
        Func<TWorker, int, TResult> work);
}
