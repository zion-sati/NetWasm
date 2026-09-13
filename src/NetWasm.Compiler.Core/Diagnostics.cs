using System;

namespace NetWasm.Compiler.Core;

public enum DiagnosticCode
{
    InvalidCommandLine = 1000,
    UnsupportedCil = 1001,
    InvalidCil = 1002,
    UnsupportedMetadata = 1003,
    AssemblyResolution = 1004,
    DuplicateAssembly = 1005,
    InvalidEntryPoint = 1006,
    IrreducibleControlFlow = 1007,
    RuntimeContract = 1008,
    ComponentContract = 1009,
    ComponentToolchain = 1010,
    CompilerInvariant = 1011,
    GenericExpansion = 2001,
}

public sealed record CompilerDiagnostic(
    DiagnosticCode Code,
    string Message,
    string? Method = null,
    int? IlOffset = null)
{
    public string Id => Code == DiagnosticCode.GenericExpansion
        ? "NWA2001"
        : $"NW{(int)Code:0000}";

    public override string ToString()
    {
        var location = Method is null
            ? string.Empty
            : IlOffset is null
                ? $"{Method}: "
                : $"{Method} IL_{IlOffset.Value:x4}: ";
        return $"{Id}: {location}{Message}";
    }
}

public sealed class CompilerException(CompilerDiagnostic diagnostic) : Exception(diagnostic.ToString())
{
    public CompilerDiagnostic Diagnostic { get; } = diagnostic;
}
