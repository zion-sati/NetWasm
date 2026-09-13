using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Pipeline;

internal interface ICompilationLayoutBuilder
{
    CompilationLayouts Compile(
        MetadataCompilationSnapshot metadata,
        ReachableProgram program,
        WasmTarget target);
}
