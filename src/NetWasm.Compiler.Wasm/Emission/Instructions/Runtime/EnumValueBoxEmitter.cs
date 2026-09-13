using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumValueBoxEmitter(
    ITypeLayoutProvider typeLayouts,
    IValueLayoutProvider values,
    IRuntimeImportResolver runtimeImports,
    IAddressInstructionEmitter addresses,
    ITargetLayout layouts) : IEnumValueBoxEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        EnumMetadataLayout entry,
        int valueLocal,
        int resultLocal)
    {
        var valueLayout = values.GetValueLayout(entry.UnderlyingType);
        var objectLayout = typeLayouts.GetObjectLayout(entry.Type);
        var payload = WasmTargetLayout.Align(
            layouts.Target.ObjectHeaderSize,
            valueLayout.Alignment);
        addresses.Emit(code, objectLayout.Size);
        WriteI32(code, entry.TypeId);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(
                (uint)runtimeImports.Resolve(RuntimeImportSymbol.Allocate))));
        Set(code, resultLocal);
        Get(code, resultLocal);
        addresses.Emit(code, payload);
        addresses.Emit(code, AddressOperation.Add);
        Get(code, valueLocal);
        ManagedMemoryEmitter.EmitStoreBySize(
            code,
            layouts.Target,
            0,
            valueLayout.Size);
    }

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void WriteI32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));
}
