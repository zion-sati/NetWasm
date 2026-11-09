using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IEntryPointEmitter
{
    byte[] Emit(
        MethodDefinitionModel entryPoint,
        RuntimeInitializationPlan initialization,
        bool hasFinalizers,
        IFunctionIndexResolver functionIndices,
        bool reportTerminalExceptions = true,
        EntityKey? argumentFactory = null,
        MethodInstanceModel? completion = null,
        EntityKey? localTimePreflight = null,
        EntityKey? processExit = null,
        bool initializesRuntime = true);
}
