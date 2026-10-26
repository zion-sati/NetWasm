using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

// The body of the module's one runtime initializer, which every entry calls. Its
// registrations scale with the program's types, so no entry carries its own copy.
internal sealed class RuntimeInitializerFunctionEmitter(
    ITargetLayout layouts,
    ITypeDescriptorSource descriptors,
    IStaticDataLayout staticData,
    IRuntimeImportResolver runtimeImports,
    IAddressInstructionEmitter addresses,
    IRuntimeCoreInitializer runtimeCore,
    IGeneratedFunctionWriterFactory writers) : IRuntimeInitializerFunctionEmitter
{
    public byte[] Emit(RuntimeInitializationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var code = writers.Create();
        WasmLocalDeclarationWriter.Write(code.Bytes, [], layouts.Target);
        Initialize(code, plan);
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return code.Snapshots.Read();
    }

    private void Initialize(
        GeneratedFunctionWriterLease code,
        RuntimeInitializationPlan plan)
    {
        runtimeCore.Initialize(code, plan.StaticDataEnd);
        if (plan.RuntimeImportSelection.IncludeFinalization)
        {
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                    RuntimeImportSymbol.InitializeFinalization,
                    plan.RuntimeImportSelection))));
        }
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
            var guardAddress = plan.StackTraceSymbolRegistrationGuardAddress ??
                throw new InvalidOperationException(
                    "Stack-trace symbol registration requires a guard address.");
            EmitOnce(code, guardAddress, () =>
            {
                foreach (var symbol in plan.StackTraceSymbols)
                {
                    code.Instructions.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.I32Constant,
                        WasmInstructionOperand.Signed(symbol.Id)));
                    addresses.Emit(code.Instructions, symbol.Address);
                    code.Instructions.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.I32Constant,
                        WasmInstructionOperand.Signed(symbol.Length)));
                    code.Instructions.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.Call,
                        WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                            RuntimeImportSymbol.StackTraceRegisterSymbol,
                            plan.RuntimeImportSelection))));
                }
            });
        }
        if (plan.StateRegistrationGuardAddress is int registrationGuard)
        {
            EmitOnce(code, registrationGuard, () => RegisterState(code));
        }
        else
        {
            RegisterState(code);
        }
        if (plan.NativeCallbackReadinessAddress is int readinessAddress)
        {
            addresses.Emit(code.Instructions, readinessAddress);
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(1)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Store,
                WasmInstructionOperand.Memory(2, 0)));
        }
        foreach (var initializer in plan.ModuleInitializers)
        {
            InitializeModule(code, initializer);
        }
    }

    // Registrations persist for the instance, so a zero-initialized module-data flag
    // limits them to the first entry that runs this initializer.
    private void EmitOnce(GeneratedFunctionWriterLease code, int guardAddress, Action emitBody)
    {
        addresses.Emit(code.Instructions, guardAddress);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, 0)));
        code.Instructions.Write(WasmInstruction.NoOperand(
            WasmOpcodes.I32EqualZero));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        emitBody();
        addresses.Emit(code.Instructions, guardAddress);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(1)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Store,
            WasmInstructionOperand.Memory(2, 0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void RegisterState(GeneratedFunctionWriterLease code)
    {
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
        foreach (var descriptor in descriptors.MetadataTypeDescriptors)
        {
            // Metadata-only identities can be materialized as System.Type objects but
            // are never valid allocation types. A zero-sized descriptor registers the
            // semantic ID without adding a managed object layout.
            EmitTypeDescriptor(
                code,
                descriptor.TypeId,
                baseTypeId: 0,
                objectSize: 0,
                bitmapAddress: 0,
                bitmapBitCount: 0,
                assignableTypeIdsAddress: 0,
                assignableTypeIdCount: 0,
                hasFinalizer: false,
                isInterface: false);
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
