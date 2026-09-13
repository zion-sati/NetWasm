using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal static class ManagedMethodBodyKey
{
    public static string Resolve(StructuredMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return Resolve(method.Header);
    }

    public static string Resolve(StructuredMethodHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return header.MethodInstance?.CanonicalName ?? header.Method.Key.ToString();
    }

}
