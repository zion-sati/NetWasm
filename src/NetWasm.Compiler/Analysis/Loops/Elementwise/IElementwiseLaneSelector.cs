using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops.Elementwise;

internal interface IElementwiseLaneSelector
{
    // The one lane kind a counted loop's elements, operations, folds and copies share and vectorize exactly, with no
    // array written at one offset and accessed at another; null when there is none.
    CliValueKind? Select(ElementwiseBody body, ImmutableArray<CliValueKind> locals);
}
