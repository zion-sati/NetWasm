using NetWasm.Compiler.ExceptionTypes;

namespace NetWasm.Compiler;

public sealed partial record CompilationResult
{
    public DiagnosticArtifactBundle? DiagnosticArtifacts { get; init; }
}
