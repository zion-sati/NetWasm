using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IAsyncJSExportStatusEmitter
{
    byte[] Emit(JavaScriptAsyncMethodBinding binding);
}
