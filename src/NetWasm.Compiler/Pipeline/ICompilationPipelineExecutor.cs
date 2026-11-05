using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Pipeline;

internal interface ICompilationPipelineExecutor
{
    // A compile; one that may be served from the last compile gives only its module then.
    CompilationOutcome Execute(CompilerOptions options, bool serveFromLastCompile = false);
}
