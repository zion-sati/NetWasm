namespace NetWasm.Compiler.Cli;

internal interface ICompileCliArtifactPlanner
{
    CompileCliArtifactPlan Plan(
        CompileCliOptions options,
        CompilationResult result);
}
