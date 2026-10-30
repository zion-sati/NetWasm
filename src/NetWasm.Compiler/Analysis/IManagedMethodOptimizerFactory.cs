using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal interface IManagedMethodOptimizerFactory
{
    IManagedMethodOptimizer Create(
        IMethodBodyReader methodBodies,
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods,
        IMethodSpecializer specializer,
        ICalledMethodResolver calledMethods,
        IRuntimeIntrinsicRegistry intrinsics,
        IMethodSpecializer constantCallFolding,
        IDispatchTargetResolver dispatchTargets,
        IFinalizerResolver finalizers,
        ITypeDefinitionResolver typeDefinitions,
        Loops.IArrayLoopMethodResolver arrayLoopMethods,
        Loops.IVectorLoopMethodResolver vectorLoopMethods,
        WasmOptimizationSettings settings);
}
