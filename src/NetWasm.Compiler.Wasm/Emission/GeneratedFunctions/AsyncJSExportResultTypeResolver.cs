using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class AsyncJSExportResultTypeResolver(ITargetLayout layouts) :
    IAsyncJSExportResultTypeResolver
{
    public WasmFunctionType Resolve(JavaScriptAsyncMethodBinding binding)
    {
        var type = binding.Return.ResultType!.StackKind;
        if (type == CliValueKind.NativeInt)
        {
            type = layouts.Target.UsesMemory64 ? CliValueKind.I8 : CliValueKind.I4;
        }
        return WasmFunctionType.Create(type, CliValueKind.I4);
    }
}
