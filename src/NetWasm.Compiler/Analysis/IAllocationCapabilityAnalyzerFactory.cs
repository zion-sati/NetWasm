using NetWasm.Compiler.Core;
using NetWasm.Compiler.GarbageCollection;

namespace NetWasm.Compiler.Analysis;

internal interface IAllocationCapabilityAnalyzerFactory
{
    IAllocationCapabilityAnalyzer Create(
        ITypeRepository types,
        IFieldRepository fields,
        IMethodRepository methods);
}
