namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Encoding;

/// <summary>
/// Composition boundary for the stateful binary and instruction writers.
/// </summary>
internal sealed class GeneratedFunctionWriterFactory : IGeneratedFunctionWriterFactory
{
    public GeneratedFunctionWriterLease Create()
    {
        var buffer = new WasmBinaryBuffer();
        var bytes = new WasmBinaryWriter(buffer);
        return new GeneratedFunctionWriterLease(
            bytes,
            new WasmBinarySnapshotReader(buffer),
            new WasmInstructionWriter(bytes));
    }
}
