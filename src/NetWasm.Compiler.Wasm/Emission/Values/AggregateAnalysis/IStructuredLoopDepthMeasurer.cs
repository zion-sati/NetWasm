using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal interface IStructuredLoopDepthMeasurer
{
    // How many loops enclose each block; a replicated block takes its deepest occurrence.
    Dictionary<StructuredBlockId, int> Measure(StructuredSequence body);
}
