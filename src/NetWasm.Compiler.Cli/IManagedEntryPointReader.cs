namespace NetWasm.Compiler.Cli;

internal interface IManagedEntryPointReader
{
    ManagedEntryPoint Read(string assemblyPath);
}
