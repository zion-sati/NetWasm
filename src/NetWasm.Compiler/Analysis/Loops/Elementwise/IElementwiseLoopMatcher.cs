using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops.Elementwise;

internal interface IElementwiseLoopMatcher
{
    // The counted loop whose entry jump is at `jump` and whose test starts at `condition`, if the vector loop can
    // take it.
    ElementwiseLoop? Match(ImmutableArray<CilInstruction> instructions, int jump, int condition, ImmutableArray<CliValueKind> locals);
}
