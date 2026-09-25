using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal sealed record StaticInitializationEmissionRequest(
    EntityKey TypeDefinition,
    CliTypeIdentity? DeclaringType,
    ModuleDataPlan ModuleData,
    bool IsStaticMethodCall = false);
