namespace NetWasm.Compiler.Diagnostics;

internal sealed record CompilerDiagnosticScope(string? Path)
{
    public bool IsEnabled => !string.IsNullOrWhiteSpace(Path);
}
