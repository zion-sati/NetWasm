using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IExceptionGroupEnumerator
{
    IEnumerable<StructuredExceptionGroupId> Enumerate(StructuredMethod method);
}
