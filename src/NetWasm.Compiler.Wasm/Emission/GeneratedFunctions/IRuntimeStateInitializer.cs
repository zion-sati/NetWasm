namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IRuntimeStateInitializer
{
    void Initialize(
        GeneratedFunctionWriterLease code,
        RuntimeInitializationPlan plan);
}
