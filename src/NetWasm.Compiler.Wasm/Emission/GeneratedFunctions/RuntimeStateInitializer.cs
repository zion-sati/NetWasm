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
                descriptor.AssignableTypeIdsAddress,
                descriptor.AssignableTypeIdCount,
                descriptor.Finalizer is not null,
                descriptor.IsInterface);
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
                descriptor.AssignableTypeIdsAddress,
                descriptor.AssignableTypeIdCount,
                descriptor.Finalizer is not null,
                descriptor.IsInterface);
        }
        foreach (var descriptor in descriptors.ValueTypeDescriptors)
        {
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(descriptor.TypeId)));
            addresses.Emit(code.Instructions, descriptor.Size);
            addresses.Emit(code.Instructions, descriptor.BoxedPayloadOffset);
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

    private static void InitializeModule(
        GeneratedFunctionWriterLease code,
        ModuleInitializerCall initializer)
    {
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)initializer.FunctionIndex)));
    }

    private void EmitTypeDescriptor(
        GeneratedFunctionWriterLease code,
        int typeId,
        int baseTypeId,
        int objectSize,
        int bitmapAddress,
        int bitmapBitCount,
        int assignableTypeIdsAddress,
        int assignableTypeIdCount,
        bool hasFinalizer,
        bool isInterface)
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
        addresses.Emit(code.Instructions, assignableTypeIdsAddress);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(assignableTypeIdCount)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(hasFinalizer ? 1 : 0)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(isInterface ? 1 : 0)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.RegisterType))));
    }
}
