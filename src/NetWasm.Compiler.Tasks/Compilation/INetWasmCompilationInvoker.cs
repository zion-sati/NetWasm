namespace NetWasm.Compiler.Tasks.Compilation;

internal interface INetWasmCompilationInvoker
{
    ManagedModuleCompilation Compile(CompilerOptions options);
}
