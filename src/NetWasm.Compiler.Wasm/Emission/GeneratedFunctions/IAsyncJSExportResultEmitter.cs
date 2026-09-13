using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IAsyncJSExportResultEmitter
{
    byte[] Emit(JavaScriptAsyncMethodBinding binding);
}
