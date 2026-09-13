using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.EntryPoints;

public sealed record CompilationEntry(
    MethodDefinitionModel EntryPoint,
    ImmutableArray<ProgramExport> Exports);
