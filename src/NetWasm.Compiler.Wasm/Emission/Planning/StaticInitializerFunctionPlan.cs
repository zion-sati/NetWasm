using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed record StaticInitializerFunctionPlan(
    ImmutableArray<StaticInitializerFunction> Initializers,
    int CatchMetadataAddress,
    int FailureFunctionIndex);

internal sealed record StaticInitializerFunction(
    string Key,
    int FunctionIndex,
    int InitializerIndex,
    int GuardAddress);
