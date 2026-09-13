using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class HostCallbackFunctionTypeResolver :
    IHostCallbackFunctionTypeResolver
{
    public WasmFunctionType Resolve(MethodInstanceModel invoke) =>
        WasmFunctionType.Create(
            invoke.Signature.ReturnType,
            [CliValueKind.I4, .. invoke.Signature.ParameterSignatureTypes.Select(
                type => InteropTypeClassifier.IsString(type) ||
                        InteropTypeClassifier.IsByteArray(type)
                    ? CliValueKind.I4
                    : type.StackKind)]);
}
