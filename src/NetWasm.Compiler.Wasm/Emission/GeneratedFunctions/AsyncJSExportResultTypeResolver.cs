using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class AsyncJSExportResultTypeResolver(ITargetLayout layouts) :
    IAsyncJSExportResultTypeResolver
{
    public WasmFunctionType Resolve(JavaScriptAsyncMethodBinding binding)
    {
        // A JSObject result leaves as its host handle, as a synchronous one does.
        var result = binding.Return.ResultType!;
        var type = InteropTypeClassifier.IsJSObject(result) || InteropTypeClassifier.IsHandleValue(result)
            ? CliValueKind.I4
            : InteropTypeClassifier.IsConvertedValue(result)
                ? InteropTypeClassifier.ConvertedResultKind(result)
                : result.StackKind;
        if (type == CliValueKind.NativeInt)
        {
            type = layouts.Target.UsesMemory64 ? CliValueKind.I8 : CliValueKind.I4;
        }
        return WasmFunctionType.Create(type, CliValueKind.I4);
    }
}
