using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed class ArgumentTypeResolver(ITypeRepository types) :
    IArgumentTypeResolver
{
    public CliValueKind Resolve(StructuredMethodHeader header, int index)
    {
        ArgumentNullException.ThrowIfNull(header);
        var method = header.Method;
        var signature = header.MethodInstance?.Signature ?? method.Signature;
        if (!method.IsStatic)
        {
            if (index == 0)
            {
                return (header.MethodInstance?.DeclaringType.IsValueType ??
                        types.GetTypeDefinition(method.DeclaringType).IsValueType)
                    ? CliValueKind.ManagedAddress
                    : CliValueKind.ManagedReference;
            }
            index--;
        }
        return signature.ParameterTypes[index];
    }
}
