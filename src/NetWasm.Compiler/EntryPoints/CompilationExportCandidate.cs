using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.EntryPoints;

internal sealed record CompilationExportCandidate(
    ProgramExport Export,
    DiagnosticCode DuplicateDiagnosticCode,
    string DuplicateMessage,
    MethodDefinitionModel? DuplicateMethod);
