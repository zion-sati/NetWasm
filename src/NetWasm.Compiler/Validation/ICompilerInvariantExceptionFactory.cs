using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Validation;

internal interface ICompilerInvariantExceptionFactory
{
    CompilerException Create(
        string message,
        string? method = null,
        int? ilOffset = null);
}
