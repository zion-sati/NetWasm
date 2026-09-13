using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class AsyncJSExportWrapperEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider valueLayouts,
    IRuntimeImportResolver runtimeImports,
    IRuntimeStateInitializer runtimeInitialization,
    IImplicitExceptionEmitter exceptions,
    IReferenceComparisonEmitter references,
    IGeneratedFunctionWriterFactory writers) : IAsyncJSExportWrapperEmitter
{
    public byte[] Emit(
        MethodDefinitionModel method,
        JavaScriptAsyncMethodBinding binding,
        RuntimeInitializationPlan initialization,
        bool hasFinalizers,
        IFunctionIndexResolver functionIndices,
        EntityKey? argumentFactory = null)
    {
        var wrapperParameterCount = argumentFactory is null
            ? method.Signature.ParameterTypes.Length
            : 0;
        var taskLocal = wrapperParameterCount;
        var handleLocal = taskLocal + 1;
        var rootFrameLocal = handleLocal + 1;
        var valueFrameLocal = rootFrameLocal + 1;
        var finalizerExceptionLocal = valueFrameLocal + 1;
        var code = writers.Create();
        WasmLocalDeclarationWriter.Write(
            code.Bytes,
            [
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedReference,
            ],
            layouts.Target);
        runtimeInitialization.Initialize(code, initialization);
        if (binding.Return.Kind == JavaScriptAsyncReturnKind.ValueTask)
        {
            var valueLayout = valueLayouts.GetValueLayout(
                method.Signature.ReturnSignatureType);
            ManagedMemoryEmitter.EmitAddressConstant(
                code.Instructions,
                layouts.Target,
                valueLayout.Size);
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                    RuntimeImportSymbol.ValueFrameEnter))));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)valueFrameLocal)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)valueFrameLocal)));
            EmitArguments(
                code,
                method.Signature.ParameterTypes.Length,
                argumentFactory,
                functionIndices);
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(
                    method.Key))));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)valueFrameLocal)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(
                    binding.ValueTaskAsTask!))));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)taskLocal)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)valueFrameLocal)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                    RuntimeImportSymbol.ValueFrameLeave))));
        }
        else
        {
            EmitArguments(
                code,
                method.Signature.ParameterTypes.Length,
                argumentFactory,
                functionIndices);
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(
                    method.Key))));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)taskLocal)));
        }

        EmitReferenceNullCheck(code, taskLocal);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(1)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.RootFrameEnter))));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)rootFrameLocal)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)rootFrameLocal)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)taskLocal)));
        ManagedMemoryEmitter.EmitStoreBySize(
            code.Instructions,
            layouts.Target,
            0,
            layouts.Target.ObjectReferenceSize);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)taskLocal)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.HandleNew))));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)handleLocal)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)rootFrameLocal)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.RootFrameLeave))));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)handleLocal)));
        references.Emit(
            code.Instructions,
            ReferenceComparison.EqualZero,
            CliValueKind.I4);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code.Instructions, ManagedExceptionKind.OutOfMemory);
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        if (hasFinalizers)
        {
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                    RuntimeImportSymbol.FinalizerSafepoint))));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalTee,
                WasmInstructionOperand.Unsigned((uint)finalizerExceptionLocal)));
            references.Emit(code.Instructions, ReferenceComparison.EqualZero);
            code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)finalizerExceptionLocal)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Throw,
                WasmInstructionOperand.Unsigned(0)));
            code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)handleLocal)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return code.Snapshots.Read();
    }

    private static void EmitArguments(
        GeneratedFunctionWriterLease code,
        int count,
        EntityKey? argumentFactory,
        IFunctionIndexResolver functionIndices)
    {
        if (argumentFactory is { } factory)
        {
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(factory))));
            return;
        }

        for (var index = 0; index < count; index++)
        {
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)index)));
        }
    }

    private void EmitReferenceNullCheck(GeneratedFunctionWriterLease code, int local)
    {
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));
        references.Emit(code.Instructions, ReferenceComparison.EqualZero);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code.Instructions, ManagedExceptionKind.NullReference);
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

}
