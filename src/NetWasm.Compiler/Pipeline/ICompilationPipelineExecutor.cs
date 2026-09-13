using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Pipeline;

internal interface ICompilationPipelineExecutor
{
    CompilationResult Execute(CompilerOptions options);
}
