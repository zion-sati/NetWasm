using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

// The body of the module's one runtime initializer, which every entry calls. Its
// registrations scale with the program's types, so no entry carries its own copy.
internal sealed class RuntimeInitializerFunctionEmitter(
    ITargetLayout layouts,
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
        }
        // CoreLib's sidecar readers find the descriptor through a static word.
        if (plan.SidecarSlotAddress is int slot && plan.SidecarDescriptorAddress is int descriptor)
        {
            addresses.Emit(code.Instructions, slot);
            addresses.Emit(code.Instructions, descriptor);
            code.Instructions.Write(layouts.Target.AddressSize == sizeof(long)
                ? WasmInstruction.WithOperand(WasmOpcodes.I64Store, WasmInstructionOperand.Memory(3, 0))
                : WasmInstruction.WithOperand(WasmOpcodes.I32Store, WasmInstructionOperand.Memory(2, 0)));
        }
        if (plan.StateRegistrationGuardAddress is int registrationGuard)
        {
            EmitOnce(code, registrationGuard, () => RegisterState(code, plan));
        }
        else
        {
            RegisterState(code, plan);
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

    private void RegisterState(GeneratedFunctionWriterLease code, RuntimeInitializationPlan plan)
    {
        // One table registers every type; the runtime keeps what its hot paths read.
        addresses.Emit(code.Instructions, staticData.TypeTableAddress);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.RegisterTypes))));
        foreach (var address in staticData.StaticRootAddresses)
        {
            addresses.Emit(code.Instructions, address);
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                    RuntimeImportSymbol.RegisterStaticRoot))));
        }
        // The static Type objects become the objects of the types they name,
        // which are registered above.
        if (staticData.TypeObjectCount != 0)
        {
            addresses.Emit(code.Instructions, staticData.TypeObjectTableAddress);
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(staticData.TypeObjectCount)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(staticData.TypeObjectSize)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                    RuntimeImportSymbol.RegisterTypeObjects,
                    plan.RuntimeImportSelection))));
        }
        // GetType on a receiver of unknown type reads the runtime's cache of
        // Type objects through this word.
        if (staticData.TypeObjectCacheAddress != 0)
        {
            addresses.Emit(code.Instructions, staticData.TypeObjectCacheAddress);
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                    RuntimeImportSymbol.TypeObjectCache,
                    plan.RuntimeImportSelection))));
            ManagedMemoryEmitter.EmitStoreBySize(
                code.Instructions,
                layouts.Target,
                0,
                layouts.Target.ObjectReferenceSize);
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
}
