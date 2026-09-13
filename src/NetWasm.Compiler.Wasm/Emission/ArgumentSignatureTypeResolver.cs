using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed class ArgumentSignatureTypeResolver(
    ICilTypeIdentityResolver identities) : IArgumentSignatureTypeResolver
{
    public CliTypeIdentity Resolve(StructuredMethodHeader header, int index)
    {
        ArgumentNullException.ThrowIfNull(header);
        var method = header.Method;
        var signature = header.MethodInstance?.Signature ?? method.Signature;
        if (!method.IsStatic)
        {
            if (index == 0)
            {
                return header.MethodInstance?.DeclaringType ??
                    identities.Resolve(method.DeclaringType);
            }
            index--;
        }
        return signature.ParameterSignatureTypes[index];
    }
}
