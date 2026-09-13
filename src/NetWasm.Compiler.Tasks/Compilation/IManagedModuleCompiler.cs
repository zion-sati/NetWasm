namespace NetWasm.Compiler.Tasks.Compilation;

internal interface IManagedModuleCompiler
{
    ManagedModuleCompilation Compile(ManagedModuleCompileRequest request);
}
