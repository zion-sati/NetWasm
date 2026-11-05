using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.GarbageCollection;

namespace NetWasm.Compiler.Analysis;

internal sealed class ManagedByRefEffectAnalyzerFactory(
    IIndexedWorkExecutor work,
    CompilerParallelism parallelism) :
    IManagedByRefEffectAnalyzerFactory
{
    private readonly IIndexedWorkExecutor _work = work ??
        throw new ArgumentNullException(nameof(work));
    private readonly CompilerParallelism _parallelism = parallelism ??
        throw new ArgumentNullException(nameof(parallelism));

    public IManagedByRefEffectAnalyzer Create(
        IMethodRepository methods,
        IRuntimeIntrinsicRegistry intrinsics) =>
        new ManagedByRefEffectAnalyzer(methods, intrinsics, _work, _parallelism.WorkerCount);
}
