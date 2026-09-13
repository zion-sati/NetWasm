using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Validation;

public interface ICompilerComplexityInvariantValidator
{
    void Validate(CompilerComplexityReport report);
}
