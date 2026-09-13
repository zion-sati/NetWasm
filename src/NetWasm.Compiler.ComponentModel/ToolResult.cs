namespace NetWasm.Compiler.ComponentModel;

public sealed record ToolResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
