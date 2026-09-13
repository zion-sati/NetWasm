using System;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed class StructuredExceptionGroupKeyFactory : IStructuredExceptionGroupKeyFactory
{
    public StructuredExceptionGroupKey Create(StructuredMethod method, StructuredExceptionGroupId group)
    {
        ArgumentNullException.ThrowIfNull(method);

        return new(
            method.Header.Method.Key,
            method.Header.MethodInstance?.CanonicalName,
            group);
    }
}
