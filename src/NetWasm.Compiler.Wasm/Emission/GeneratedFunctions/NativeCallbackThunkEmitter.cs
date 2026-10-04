using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class NativeCallbackThunkEmitter(
    ITargetLayout layouts,
    IRuntimeImportResolver runtimeImports,
    IAddressInstructionEmitter addresses,
    IStaticInitializationEmitter staticInitialization,
    IManagedTerminalExceptionBoundaryEmitter terminalExceptions,
    IGeneratedFunctionWriterFactory writers) : INativeCallbackThunkEmitter
{
    public byte[] Emit(
        NativeCallbackMethodPlan callback,
        ModuleDataPlan moduleData,
        RuntimeImportSelection runtimeImportSelection,
        IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(moduleData);
        ArgumentNullException.ThrowIfNull(functionIndices);
        var readinessAddress = moduleData.NativeCallbackReadinessAddress ??
            throw new InvalidOperationException(
                "Native callback emission requires a planned readiness word.");
        var signature = callback.Abi.PhysicalSignature;
        var code = writers.Create();
        var hasResult = signature.ReturnType != CliValueKind.Void;
        var resultLocal = signature.ParameterTypes.Length;
        var exceptionLocal = resultLocal + (hasResult ? 1 : 0);
        var rootFrameLocal = exceptionLocal + 1;
        var typeIdLocal = rootFrameLocal + 1;
        var messageLocal = typeIdLocal + 1;
        var messageLengthLocal = messageLocal + 1;
        var stackTraceLocal = messageLengthLocal + 1;
        var stackTraceLengthLocal = stackTraceLocal + 1;
        WasmLocalDeclarationWriter.Write(
            code.Bytes,
            [
                .. hasResult
                    ? [signature.ReturnType]
                    : Array.Empty<CliValueKind>(),
                CliValueKind.ManagedReference,
                CliValueKind.ManagedAddress,
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.I4,
            ],
            layouts.Target);

        addresses.Emit(code.Instructions, readinessAddress);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, 0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        terminalExceptions.Emit(
            code.Instructions,
            exceptionLocal,
            rootFrameLocal,
            typeIdLocal,
            messageLocal,
            messageLengthLocal,
            stackTraceLocal,
            stackTraceLengthLocal,
            signature.ReturnType,
            resultLocal,
            runtimeImports.Resolve(
                RuntimeImportSymbol.ManagedTerminalExceptionReport,
                runtimeImportSelection),
            runtimeImports.Resolve(
                RuntimeImportSymbol.ManagedTerminalExceptionRaise,
                runtimeImportSelection),
            () =>
            {
                staticInitialization.Emit(
                    new(
                        callback.Method.Definition.DeclaringType,
                        callback.Method.DeclaringType,
                        moduleData,
                        IsStaticMethodCall: true),
                    code.Instructions,
                    functionIndices);
                for (var index = 0; index < signature.ParameterTypes.Length; index++)
                {
                    code.Instructions.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.LocalGet,
                        WasmInstructionOperand.Unsigned((uint)index)));
                }
                code.Instructions.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.Call,
                    WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(
                        callback.Method))));
                if (hasResult)
                {
                    code.Instructions.Write(WasmInstruction.WithOperand(
                        WasmOpcodes.LocalSet,
                        WasmInstructionOperand.Unsigned((uint)resultLocal)));
                }
            });
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return code.Snapshots.Read();
    }
}
