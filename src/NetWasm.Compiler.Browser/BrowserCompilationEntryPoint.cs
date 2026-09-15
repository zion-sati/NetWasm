using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.Browser;

public sealed record BrowserCompilationEntryPoint(
    string AssemblyPath,
    string TypeName,
    string MethodName,
    int? Token,
    CompilerEntryPointKind Kind,
    ManagedExecutableEntryPointAbi? Abi = null);
