namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

// ArrayChecked: the caller has already checked the array for null, or proved it non-null.
internal readonly record struct RectangularArrayElementAddressRequest(
    InstructionEmissionRequest Instruction,
    int ArraySlot,
    bool ArrayChecked = false);
