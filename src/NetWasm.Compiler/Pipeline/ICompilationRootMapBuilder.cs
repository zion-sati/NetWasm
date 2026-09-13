using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Pipeline;

internal interface ICompilationRootMapBuilder
{
    ReachableProgram Analyze(
        MetadataCompilationSnapshot metadata,
        CompilationLayouts layouts,
        ReachableProgram program);
}
