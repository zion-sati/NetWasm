using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

// Allocates a non-finalizable object of a known layout. Where the runtime inlines
// its functions, allocate_object also takes whether the layout has references:
// with the size, that selects the collector's run, so the inlined allocation is a
// bump of a constant run. Elsewhere allocate finds the run from the registered type.
internal static class ObjectAllocationCall
{
    public static void Emit(
        IWasmInstructionWriter code,
        IAddressInstructionEmitter addresses,
        IRuntimeImportResolver runtimeImports,
        InstructionModuleTarget target,
        ObjectLayout layout) =>
        Emit(code, addresses, runtimeImports, target.ObjectAllocation, layout);

    public static void Emit(
        IWasmInstructionWriter code,
        IAddressInstructionEmitter addresses,
        IRuntimeImportResolver runtimeImports,
        ObjectAllocationPolicy policy,
        ObjectLayout layout)
    {
        addresses.Emit(code, layout.Size);
        EmitAfterSize(code, runtimeImports, policy, layout);
    }

    // The arguments after the size, which the caller has pushed, and the call.
    public static void EmitAfterSize(
        IWasmInstructionWriter code,
        IRuntimeImportResolver runtimeImports,
        ObjectAllocationPolicy policy,
        ObjectLayout layout)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(layout.TypeId)));
        var allocation = RuntimeImportSymbol.Allocate;
        if (policy.Inline)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(layout.ReferenceOffsets.IsDefaultOrEmpty ? 0 : 1)));
            allocation = RuntimeImportSymbol.AllocateObject;
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(allocation, policy.Selection))));
    }

    // The address of the run code bumps inline for the layout (allocation_run).
    public static void EmitRun(
        IWasmInstructionWriter code,
        IAddressInstructionEmitter addresses,
        IRuntimeImportResolver runtimeImports,
        InstructionModuleTarget target,
        ObjectLayout layout)
    {
        addresses.Emit(code, layout.Size);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(layout.TypeId)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(layout.ReferenceOffsets.IsDefaultOrEmpty ? 0 : 1)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.AllocationRun, target.RuntimeImportSelection))));
    }

    // The bytes the layout's object takes from its run (allocation_slot).
    public static void EmitSlot(
        IWasmInstructionWriter code,
        IAddressInstructionEmitter addresses,
        IRuntimeImportResolver runtimeImports,
        InstructionModuleTarget target,
        ObjectLayout layout)
    {
        addresses.Emit(code, layout.Size);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.AllocationSlot, target.RuntimeImportSelection))));
    }

    // Whether the collector keeps runs (allocation_runs_enabled), a constant once
    // the runtime is inlined.
    public static void EmitRunsEnabled(
        IWasmInstructionWriter code,
        IRuntimeImportResolver runtimeImports,
        InstructionModuleTarget target) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.AllocationRunsEnabled, target.RuntimeImportSelection))));

    // The bump's fallback (allocate_object_fallback), which refills the run and
    // can collect; once inlined, a direct call to the allocator.
    public static void EmitFallback(
        IWasmInstructionWriter code,
        IAddressInstructionEmitter addresses,
        IRuntimeImportResolver runtimeImports,
        InstructionModuleTarget target,
        ObjectLayout layout)
    {
        addresses.Emit(code, layout.Size);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(layout.TypeId)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.AllocateObjectFallback, target.RuntimeImportSelection))));
    }
}
