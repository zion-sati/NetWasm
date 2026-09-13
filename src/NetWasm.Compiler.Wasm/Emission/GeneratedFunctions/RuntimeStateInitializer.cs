using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class RuntimeStateInitializer(
    ITypeDescriptorSource descriptors,
    IStaticDataLayout staticData,
    IRuntimeImportResolver runtimeImports,
    IAddressInstructionEmitter addresses,
    IRuntimeCoreInitializer runtimeCore) : IRuntimeStateInitializer
{
    public void Initialize(
        GeneratedFunctionWriterLease code,
        RuntimeInitializationPlan plan)
    {
        runtimeCore.Initialize(code, plan.StaticDataEnd);
        if (!plan.StackTraceMethods.Symbols.IsEmpty)
        {
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(
                    plan.StackTraceMethods.ExceptionTraceOffset)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(
                    plan.StackTraceMethods.StringTypeId)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                    RuntimeImportSymbol.StackTraceInitialize,
                    plan.RuntimeImportSelection))));
        }
        foreach (var descriptor in descriptors.TypeDescriptors)
        {
            EmitTypeDescriptor(
                code,
                descriptor.TypeId,
                descriptor.BaseTypeId,
                descriptor.ObjectSize,
                descriptor.BitmapAddress,
                descriptor.BitmapBitCount,
                descriptor.Finalizer is not null);
        }
        foreach (var descriptor in descriptors.ConstructedTypeDescriptors)
        {
            EmitTypeDescriptor(
                code,
                descriptor.TypeId,
                descriptor.BaseTypeId,
                descriptor.ObjectSize,
                descriptor.BitmapAddress,
                descriptor.BitmapBitCount,
                descriptor.Finalizer is not null);
        }
        foreach (var descriptor in descriptors.ValueTypeDescriptors)
        {
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(descriptor.TypeId)));
            addresses.Emit(code.Instructions, descriptor.Size);
            addresses.Emit(code.Instructions, descriptor.BitmapAddress);
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(descriptor.BitmapBitCount)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                    RuntimeImportSymbol.RegisterValueType))));
        }
        foreach (var address in staticData.StaticRootAddresses)
        {
            addresses.Emit(code.Instructions, address);
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                    RuntimeImportSymbol.RegisterStaticRoot))));
        }
        foreach (var initializer in plan.ModuleInitializers)
        {
            InitializeModule(code, initializer);
        }
    }

    private void InitializeModule(
        GeneratedFunctionWriterLease code,
        ModuleInitializerCall initializer)
    {
        addresses.Emit(code.Instructions, initializer.GuardAddress);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, 0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        addresses.Emit(code.Instructions, initializer.GuardAddress);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(1)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Store,
            WasmInstructionOperand.Memory(2, 0)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)initializer.FunctionIndex)));
        addresses.Emit(code.Instructions, initializer.GuardAddress);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(2)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Store,
            WasmInstructionOperand.Memory(2, 0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitTypeDescriptor(
        GeneratedFunctionWriterLease code,
        int typeId,
        int baseTypeId,
        int objectSize,
        int bitmapAddress,
        int bitmapBitCount,
        bool hasFinalizer)
    {
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(typeId)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(baseTypeId)));
        addresses.Emit(code.Instructions, objectSize);
        addresses.Emit(code.Instructions, bitmapAddress);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(bitmapBitCount)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(hasFinalizer ? 1 : 0)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.RegisterType))));
    }
}
