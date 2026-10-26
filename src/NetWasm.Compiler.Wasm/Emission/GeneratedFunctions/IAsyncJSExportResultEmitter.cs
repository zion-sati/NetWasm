using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IAsyncJSExportResultEmitter
{
    byte[] Emit(JavaScriptAsyncMethodBinding binding, InteropImportPlan? interopImports = null);
}
