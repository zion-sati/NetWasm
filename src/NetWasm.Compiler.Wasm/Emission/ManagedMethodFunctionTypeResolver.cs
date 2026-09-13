using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed class ManagedMethodFunctionTypeResolver :
    IManagedMethodFunctionTypeResolver
{
    public WasmFunctionType Resolve(MethodDefinitionModel method)
    {
        ArgumentNullException.ThrowIfNull(method);

        var parameters = NormalizeParameterTypes(method.WasmParameterTypes);
        return method.Signature.ReturnSignatureType.StackKind == CliValueKind.ValueType
            ? new(parameters.Insert(0, CliValueKind.ManagedAddress), CliValueKind.Void)
            : new(parameters, method.Signature.ReturnType);
    }

    public WasmFunctionType Resolve(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);

        var parameters = NormalizeParameterTypes(GetWasmParameterTypes(method));
        return method.Signature.ReturnSignatureType.StackKind == CliValueKind.ValueType
            ? new(parameters.Insert(0, CliValueKind.ManagedAddress), CliValueKind.Void)
            : new(parameters, method.Signature.ReturnType);
    }

    private static ImmutableArray<CliValueKind> GetWasmParameterTypes(
        MethodInstanceModel method) => method.Definition.IsStatic
        ? method.Signature.ParameterTypes
        : method.Signature.ParameterTypes.Insert(
            0,
            method.DeclaringType.IsValueType
                ? CliValueKind.ManagedAddress
                : CliValueKind.ManagedReference);

    private static ImmutableArray<CliValueKind> NormalizeParameterTypes(
        ImmutableArray<CliValueKind> parameters) =>
        [.. parameters.Select(static parameter => parameter == CliValueKind.ValueType
            ? CliValueKind.ManagedAddress
            : parameter)];
}
