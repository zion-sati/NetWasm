namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

/// <summary>
/// Creates an isolated writer for one generated function body.
/// </summary>
internal interface IGeneratedFunctionWriterFactory
{
    GeneratedFunctionWriterLease Create();
}
