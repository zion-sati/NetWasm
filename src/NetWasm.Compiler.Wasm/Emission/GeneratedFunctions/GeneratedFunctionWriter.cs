using System;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

/// <summary>
/// Owns the two encoders for one generated function until its bytes are taken.
/// The lease is scoped to a single generation operation and is never part of a
/// request or other immutable domain data.
/// </summary>
internal sealed class GeneratedFunctionWriterLease
{
    public GeneratedFunctionWriterLease(
        IWasmBinaryWriter bytes,
        IWasmBinarySnapshotReader snapshots,
        IWasmInstructionWriter instructions)
    {
        Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        Snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        Instructions = instructions ?? throw new ArgumentNullException(nameof(instructions));
    }

    public IWasmBinaryWriter Bytes { get; }
    public IWasmBinarySnapshotReader Snapshots { get; }
    public IWasmInstructionWriter Instructions { get; }
}
