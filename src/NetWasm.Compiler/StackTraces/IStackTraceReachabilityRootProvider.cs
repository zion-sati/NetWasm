using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.StackTraces;

internal interface IStackTraceReachabilityRootProvider
{
    ReachabilityRoots Provide(
        bool stackTraces,
        bool typeNames,
        ITypeFinder types,
        IFieldRepository fields,
        IMethodRepository methods);
}
