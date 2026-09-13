namespace NetWasm.Compiler.Tasks.Compilation;

internal interface IManagedModuleCompilationSession : IDisposable
{
    ManagedModuleCompilation Compile(ManagedModuleCompileRequest request);
}
