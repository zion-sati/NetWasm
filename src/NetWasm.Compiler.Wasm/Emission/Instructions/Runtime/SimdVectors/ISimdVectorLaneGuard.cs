using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors;

internal interface ISimdVectorLaneGuard
{
    // Resolves the lanes an intrinsic works on; for an element type the vectors do not support, emits the
    // NotSupportedException upstream's software implementation throws and returns false.
    bool TryResolve(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code, out SimdVectorLanes lanes);
}
