using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

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
        ImmutableArray<EntityKey> startupPreflights = default,
        EntityKey? processExit = null,
        bool initializesRuntime = true);
}
