using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IAsyncJSExportResultTypeResolver
{
    WasmFunctionType Resolve(JavaScriptAsyncMethodBinding binding);
}
