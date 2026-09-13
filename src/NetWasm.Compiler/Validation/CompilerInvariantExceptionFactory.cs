using NetWasm.Compiler;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Validation;

internal sealed class CompilerInvariantExceptionFactory :
    ICompilerInvariantExceptionFactory
{
    public CompilerException Create(
        string message,
        string? method = null,
        int? ilOffset = null) => new(new CompilerDiagnostic(
            DiagnosticCode.CompilerInvariant,
            message,
            method,
            ilOffset));
}
