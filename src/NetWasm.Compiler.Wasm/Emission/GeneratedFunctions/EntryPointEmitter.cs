using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class EntryPointEmitter(
    ITargetLayout layouts,
    IRuntimeImportResolver runtimeImports,
    IRuntimeStateInitializer runtimeInitialization,
    IManagedTerminalExceptionBoundaryEmitter terminalExceptions,
    IGeneratedFunctionWriterFactory writers) : IEntryPointEmitter
{
    public byte[] Emit(
        MethodDefinitionModel entryPoint,
        RuntimeInitializationPlan initialization,
        bool hasFinalizers,
        IFunctionIndexResolver functionIndices,
        bool reportTerminalExceptions = true,
        EntityKey? argumentFactory = null)
    {
        var hasResult = entryPoint.Signature.ReturnType != CliValueKind.Void;
        var wrapperParameterCount = argumentFactory is null
            ? entryPoint.WasmParameterTypes.Length
            : 0;
        var resultLocal = wrapperParameterCount;
        var exceptionLocal = resultLocal + (hasResult ? 1 : 0);
        var rootFrameLocal = exceptionLocal + 1;
        var typeIdLocal = rootFrameLocal + 1;
        var messageLocal = typeIdLocal + 1;
        var messageLengthLocal = messageLocal + 1;
        var locals = hasResult
            ? new[]
            {
                entryPoint.Signature.ReturnType,
                CliValueKind.ManagedReference,
                CliValueKind.ManagedAddress,
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.I4,
            }
            : new[]
            {
                CliValueKind.ManagedReference,
                CliValueKind.ManagedAddress,
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.I4,
            };
        var code = writers.Create();
        WasmLocalDeclarationWriter.Write(
            code.Bytes,
            locals,
            layouts.Target);
        void EmitBody()
        {
            runtimeInitialization.Initialize(code, initialization);
            if (argumentFactory is { } factory)
            {
                code.Instructions.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.Call,
                    WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(
                        factory))));
            }
            else
            {
                for (var index = 0; index < entryPoint.WasmParameterTypes.Length; index++)
                {
                    code.Instructions.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.LocalGet,
                        WasmInstructionOperand.Unsigned((uint)index)));
                }
            }
            code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(
                    entryPoint.Key))));
            if (hasResult)
            {
                code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned((uint)resultLocal)));
            }
            if (hasFinalizers)
            {
                code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
                    WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                        RuntimeImportSymbol.FinalizerSafepoint,
                        initialization.RuntimeImportSelection))));
                code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee,
                    WasmInstructionOperand.Unsigned((uint)exceptionLocal)));
                EmitReferenceEqualZero(code);
                code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
                code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
                code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)exceptionLocal)));
                code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Throw,
                    WasmInstructionOperand.Unsigned(0)));
                code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            }
        }

        if (reportTerminalExceptions)
        {
            terminalExceptions.Emit(
                code.Instructions,
                exceptionLocal,
                rootFrameLocal,
                typeIdLocal,
                messageLocal,
                messageLengthLocal,
                entryPoint.Signature.ReturnType,
                resultLocal,
                runtimeImports.Resolve(
                    RuntimeImportSymbol.ManagedTerminalExceptionReport,
                    initialization.RuntimeImportSelection),
                EmitBody);
        }
        else
        {
            EmitBody();
            if (hasResult)
                code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)resultLocal)));
        }
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return code.Snapshots.Read();
    }

    private void EmitReferenceEqualZero(GeneratedFunctionWriterLease code)
    {
        code.Instructions.Write(WasmInstruction.NoOperand(
            layouts.Target.UsesMemory64
                ? WasmOpcodes.I64EqualZero
                : WasmOpcodes.I32EqualZero));
    }
}
