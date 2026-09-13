namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal readonly record struct RectangularArrayElementAddressRequest(
    InstructionEmissionRequest Instruction,
    int ArraySlot);
