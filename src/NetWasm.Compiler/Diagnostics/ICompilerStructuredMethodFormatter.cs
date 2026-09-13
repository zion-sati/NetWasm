using NetWasm.Compiler.ControlFlow;
using StructuredMethod = global::NetWasm.Compiler.ControlFlow.Structured.StructuredMethod;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerStructuredMethodFormatter
{
    string FormatStructure(StructuredMethod method);
}
