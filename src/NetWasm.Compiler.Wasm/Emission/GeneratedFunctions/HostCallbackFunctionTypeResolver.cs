using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class HostCallbackFunctionTypeResolver :
    IHostCallbackFunctionTypeResolver
{
    public WasmFunctionType Resolve(MethodInstanceModel invoke) =>
        // The delegate's handle, then its arguments as a synchronous export takes
        // them; the result leaves as a synchronous export's does.
        WasmFunctionType.Create(
            InteropTypeClassifier.IsJSObject(invoke.Signature.ReturnSignatureType) ||
            InteropTypeClassifier.IsHandleValue(invoke.Signature.ReturnSignatureType)
                ? CliValueKind.I4
                : InteropTypeClassifier.IsConvertedValue(invoke.Signature.ReturnSignatureType)
                    ? InteropTypeClassifier.ConvertedResultKind(invoke.Signature.ReturnSignatureType)
                    : invoke.Signature.ReturnType,
            [CliValueKind.I4, .. invoke.Signature.ParameterSignatureTypes.SelectMany(
                type => InteropTypeClassifier.TransportKinds(type, type.StackKind))]);
}
