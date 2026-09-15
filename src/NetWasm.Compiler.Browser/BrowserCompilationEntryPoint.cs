namespace NetWasm.Compiler.Browser;

public sealed record BrowserCompilationEntryPoint(
    string AssemblyPath,
    string TypeName,
    string MethodName,
    int? Token,
    CompilerEntryPointKind Kind);
