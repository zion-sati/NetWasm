using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static DelegateEmitterTestSupport;
using static EmitterTestSupport;

public sealed class DelegateArgumentRootEmitterTests
{
    private const int Frame = 9;

    // The frame's first slot holds the delegate, past a value-type result's hidden pointer;
    // then each reference argument and each reference a value-type argument holds, at its
    // offset. A value type without references takes no slot.
    [Fact]
    public void RootsTheDelegateAndEveryReferenceItsArgumentsHoldOnMemory64()
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm64);
        var withReferences = CliTypeIdentity.Named(Assembly, "Test", "WithReferences", isValueType: true);
        var withoutReferences = CliTypeIdentity.Named(Assembly, "Test", "WithoutReferences", isValueType: true);
        var invoke = CreateInvoke(CallbackType(),
            CliTypeIdentity.Named(Assembly, "Test", "Result", isValueType: true),
            withReferences, withoutReferences, CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference));
        var code = new RecordingInstructionWriter();

        Emit(layouts, new ReferenceValueLayoutProvider(withReferences), code, invoke);

        Assert.Equal(
        [
            I32(4), Call(RuntimeImport(RuntimeImportSymbol.RootFrameEnter)), LocalSet(Frame),
            LocalGet(Frame), LocalGet(1), Memory(WasmOpcodes.I64Store, 3, 0),
            LocalGet(Frame), I64(8), Op(WasmOpcodes.I64Add), LocalGet(2), Memory(WasmOpcodes.I64Load, 3, 0),
            Memory(WasmOpcodes.I64Store, 3, 0),
            LocalGet(Frame), I64(16), Op(WasmOpcodes.I64Add), LocalGet(2), Memory(WasmOpcodes.I64Load, 3, 8),
            Memory(WasmOpcodes.I64Store, 3, 0),
            LocalGet(Frame), I64(24), Op(WasmOpcodes.I64Add), LocalGet(4), Memory(WasmOpcodes.I64Store, 3, 0),
        ], code.ToInstructions());
    }

    // A scalar argument takes no slot.
    [Fact]
    public void RootsTheDelegateAndEveryReferenceItsArgumentsHoldOnMemory32()
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm32);
        var withReferences = CliTypeIdentity.Named(Assembly, "Test", "WithReferences", isValueType: true);
        var invoke = CreateInvoke(CallbackType(), CliTypeIdentity.FromStackKind(CliValueKind.Void),
            CliTypeIdentity.FromStackKind(CliValueKind.I4), CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference),
            withReferences);
        var code = new RecordingInstructionWriter();

        Emit(layouts, new ReferenceValueLayoutProvider(withReferences), code, invoke);

        Assert.Equal(
        [
            I32(4), Call(RuntimeImport(RuntimeImportSymbol.RootFrameEnter)), LocalSet(Frame),
            LocalGet(Frame), LocalGet(0), Memory(WasmOpcodes.I32Store, 2, 0),
            LocalGet(Frame), I32(4), Op(WasmOpcodes.I32Add), LocalGet(2), Memory(WasmOpcodes.I32Store, 2, 0),
            LocalGet(Frame), I32(8), Op(WasmOpcodes.I32Add), LocalGet(3), Memory(WasmOpcodes.I32Load, 2, 0),
            Memory(WasmOpcodes.I32Store, 2, 0),
            LocalGet(Frame), I32(12), Op(WasmOpcodes.I32Add), LocalGet(3), Memory(WasmOpcodes.I32Load, 2, 8),
            Memory(WasmOpcodes.I32Store, 2, 0),
        ], code.ToInstructions());
    }

    private static void Emit(RecordingLayoutProvider layouts, IValueLayoutProvider values,
        RecordingInstructionWriter code, MethodInstanceModel invoke) =>
        ((IDelegateArgumentRootEmitter)new DelegateArgumentRootEmitter(layouts, values, WasmRuntimeImports.CreateCatalog()))
            .Emit(code, invoke, Frame);

    private static WasmInstruction I32(int value) =>
        WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value));

    private static WasmInstruction I64(long value) =>
        WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(value));

    private static WasmInstruction Op(byte opcode) => WasmInstruction.NoOperand(opcode);

    private static WasmInstruction Memory(byte opcode, uint alignment, uint offset) =>
        WasmInstruction.WithOperand(opcode, WasmInstructionOperand.Memory(alignment, offset));
}
