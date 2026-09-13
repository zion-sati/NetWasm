using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IAsyncJSImportResolveTypeResolver
{
    WasmFunctionType Resolve(JavaScriptAsyncMethodBinding binding);
}
