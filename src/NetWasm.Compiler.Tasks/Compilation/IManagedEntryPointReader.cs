namespace NetWasm.Compiler.Tasks.Compilation;

internal interface IManagedEntryPointReader
{
    ManagedEntryPoint Read(string assemblyPath);
}
