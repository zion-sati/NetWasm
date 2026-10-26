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
        // References cross as host handles, dates as Unix milliseconds and nullables
        // as their presence and value; a nullable result leaves in a record.
        var result = method.Signature.ReturnSignatureType;
        return new(
            [.. method.Signature.ParameterSignatureTypes.SelectMany((type, index) =>
                InteropTypeClassifier.TransportKinds(type, Normalize(method.WasmParameterTypes[index])))],
            InteropTypeClassifier.IsJSObject(result) || InteropTypeClassifier.IsHandleValue(result)
                ? CliValueKind.I4
                : InteropTypeClassifier.IsConvertedValue(result)
                    ? InteropTypeClassifier.ConvertedResultKind(result)
                    : method.Signature.ReturnType);
    }

    private static CliValueKind Normalize(CliValueKind kind) =>
        kind == CliValueKind.ValueType ? CliValueKind.ManagedAddress : kind;
}
