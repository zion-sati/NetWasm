namespace NetWasm.Compiler.Wasm.Encoding;

/// <summary>
/// Reads an immutable snapshot of bytes accumulated by a binary buffer.
/// </summary>
public interface IWasmBinarySnapshotReader
{
    byte[] Read();
}
