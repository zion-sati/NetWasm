using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops.Elementwise;

internal interface IElementwiseLoopPrefixEmitter
{
    // The checks and the vector loop that run ahead of a counted loop's entry jump, whose test is at `test`;
    // `depth` is the stack they need.
    List<CilInstruction> Emit(
        int test,
        ElementwiseLoop loop,
        ImmutableArray<CliValueKind>.Builder locals,
        ImmutableArray<CliTypeIdentity>.Builder localTypes,
        out int depth);
}
