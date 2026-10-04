using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class SynchronousJSExportFunctionTypeResolver :
    ISynchronousJSExportFunctionTypeResolver
{
    public WasmFunctionType Resolve(MethodDefinitionModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return new(
            [.. method.Signature.ParameterSignatureTypes.Select((type, index) =>
                InteropTypeClassifier.IsString(type) ||
                InteropTypeClassifier.IsByteArray(type)
                    ? CliValueKind.I4
                    : Normalize(method.WasmParameterTypes[index]))],
            method.Signature.ReturnType);
    }

    private static CliValueKind Normalize(CliValueKind kind) =>
        kind == CliValueKind.ValueType ? CliValueKind.ManagedAddress : kind;
}
