using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class EntryPointEmitter(
    ITargetLayout layouts,
    IRuntimeImportResolver runtimeImports,
    IRuntimeStateInitializer runtimeInitialization,
    IManagedTerminalExceptionBoundaryEmitter terminalExceptions,
    IManagedTerminalTrapBoundaryEmitter terminalTraps,
    IGeneratedFunctionWriterFactory writers) : IEntryPointEmitter
{
    public byte[] Emit(
        MethodDefinitionModel entryPoint,
        RuntimeInitializationPlan initialization,
        bool hasFinalizers,
        IFunctionIndexResolver functionIndices,
        bool reportTerminalExceptions = true,
        EntityKey? argumentFactory = null,
        MethodInstanceModel? completion = null,
        EntityKey? localTimePreflight = null,
        EntityKey? processExit = null,
        bool initializesRuntime = true)
    {
        // A completion receives the entry point's result and returns the process result in its
        // place, as AsyncEntryPoint.Run does for an asynchronous Main's task.
        var resultKind = completion?.Signature.ReturnType ?? entryPoint.Signature.ReturnType;
        var hasResult = resultKind != CliValueKind.Void;
        var wrapperParameterCount = argumentFactory is null
            ? entryPoint.WasmParameterTypes.Length
            : 0;
        var resultLocal = wrapperParameterCount;
        var exceptionLocal = resultLocal + (hasResult ? 1 : 0);
        var rootFrameLocal = exceptionLocal + 1;
        var typeIdLocal = rootFrameLocal + 1;
        var messageLocal = typeIdLocal + 1;
        var messageLengthLocal = messageLocal + 1;
        var stackTraceLocal = messageLengthLocal + 1;
        var stackTraceLengthLocal = stackTraceLocal + 1;
        var locals = hasResult
            ? new[]
            {
                resultKind,
                CliValueKind.ManagedReference,
                CliValueKind.ManagedAddress,
                CliValueKind.I4,
                CliValueKind.ManagedReference,
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
            // A callback-lifted entry's callback runs after its start initialized the runtime.
            if (initializesRuntime)
            {
                runtimeInitialization.Initialize(code, initialization);
            }
            // The local-time preflight may wait on wasi:filesystem, which only an
            // async-typed call such as wasi:cli/run may do; initialization may not.
            if (localTimePreflight is { } preflight)
            {
                code.Instructions.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.Call,
                    WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(preflight))));
            }
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
            if (completion is not null)
            {
                code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
                    WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(completion))));
            }
            if (hasResult)
            {
                code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned((uint)resultLocal)));
            }
            // The process is exiting: CoreLib ends the standard streams it opened and, given an
            // int Main's result, exits with it when it is not zero.
            if (processExit is { } exit)
            {
                if (hasResult)
                {
                    code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                        WasmInstructionOperand.Unsigned((uint)resultLocal)));
                }
                code.Instructions.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.Call,
                    WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(exit))));
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
            if (initialization.RuntimeImportSelection.IncludeTerminalExceptionReporter)
            {
                terminalExceptions.Emit(
                    code.Instructions,
                    exceptionLocal,
                    rootFrameLocal,
                    typeIdLocal,
                    messageLocal,
                    messageLengthLocal,
                    stackTraceLocal,
                    stackTraceLengthLocal,
                    resultKind,
                    resultLocal,
                    runtimeImports.Resolve(
                        RuntimeImportSymbol.ManagedTerminalExceptionReport,
                        initialization.RuntimeImportSelection),
                    initialization.RuntimeImportSelection.IncludeTerminalExceptionRaise
                        ? runtimeImports.Resolve(
                            RuntimeImportSymbol.ManagedTerminalExceptionRaise,
                            initialization.RuntimeImportSelection)
                        : null,
                    EmitBody);
            }
            else
            {
                terminalTraps.Emit(
                    code.Instructions,
                    resultKind,
                    resultLocal,
                    EmitBody,
                    initialization.RuntimeImportSelection.Includes(RuntimeImportSymbol.ReportUnhandledException)
                        ? runtimeImports.Resolve(
                            RuntimeImportSymbol.ReportUnhandledException,
                            initialization.RuntimeImportSelection)
                        : null);
            }
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
