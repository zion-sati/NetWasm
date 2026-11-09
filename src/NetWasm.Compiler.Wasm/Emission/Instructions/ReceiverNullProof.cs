namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal static class ReceiverNullProof
{
    // Whether analysis proved the object reference the instruction dereferences
    // non-null, so its null check can go: an earlier access already checked it,
    // newobj produced it, or it is the this of a constructor that only newobj and
    // chained constructors call.
    public static bool Proves(InstructionEmissionRequest request) =>
        request.Header.ArrayBoundsProof is { } proof &&
        proof.Describes(request.Header.Instructions, request.Header.Locals, request.Header.LocalSignatureTypes) &&
        proof.ProvesNonNull(request.Instruction.Offset) && proof.Contains(request.Instruction);
}
