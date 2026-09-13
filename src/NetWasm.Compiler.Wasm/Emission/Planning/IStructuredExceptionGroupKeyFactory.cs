using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IStructuredExceptionGroupKeyFactory
{
    StructuredExceptionGroupKey Create(StructuredMethod method, StructuredExceptionGroupId group);
}
