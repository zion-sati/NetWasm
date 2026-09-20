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
