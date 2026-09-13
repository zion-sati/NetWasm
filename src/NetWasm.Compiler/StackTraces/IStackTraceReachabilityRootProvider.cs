using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.StackTraces;

internal interface IStackTraceReachabilityRootProvider
{
    ReachabilityRoots Provide(
        bool enabled,
        ITypeFinder types,
        IFieldRepository fields);
}
