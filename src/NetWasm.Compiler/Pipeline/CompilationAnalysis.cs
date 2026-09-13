using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Pipeline;

internal sealed class CompilationAnalysis(ReachableProgram program)
{
    public ReachableProgram Program { get; } = program ??
        throw new ArgumentNullException(nameof(program));
}
