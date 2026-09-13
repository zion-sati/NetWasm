using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IAsyncJSExportWrapperEmitter
{
    byte[] Emit(
        MethodDefinitionModel method,
        JavaScriptAsyncMethodBinding binding,
        RuntimeInitializationPlan initialization,
        bool hasFinalizers,
        IFunctionIndexResolver functionIndices,
        EntityKey? argumentFactory = null);
}
