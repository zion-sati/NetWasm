using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using StructuredMethod = global::NetWasm.Compiler.ControlFlow.Structured.StructuredMethod;

namespace NetWasm.Compiler.Diagnostics;

internal interface IStructuredBlockOwnershipAnalyzer
{
    ImmutableArray<StructuredBlockOwnershipVisit> Analyze(StructuredMethod method);
}
