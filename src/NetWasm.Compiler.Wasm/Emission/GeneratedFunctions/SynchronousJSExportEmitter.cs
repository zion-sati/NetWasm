using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class SynchronousJSExportEmitter(
    ITargetLayout layouts,
    IRuntimeImportResolver runtimeImports,
    IRuntimeStateInitializer runtimeInitialization,
    IJavaScriptReferenceArgumentEmitter referenceArguments,
    IJavaScriptValueFramePlanner valueFramePlanner,
    IJavaScriptValueFrameEmitter valueFrames,
    IManagedTerminalExceptionBoundaryEmitter terminalExceptions,
    IManagedTerminalTrapBoundaryEmitter terminalTraps,
    IAddressInstructionEmitter addresses,
    IGeneratedFunctionWriterFactory writers) : ISynchronousJSExportEmitter
{
    public byte[] Emit(
        MethodDefinitionModel method,
        RuntimeInitializationPlan initialization,
        bool hasFinalizers,
        IFunctionIndexResolver functionIndices,
        InteropImportPlan interopImports)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(initialization);
        ArgumentNullException.ThrowIfNull(functionIndices);
        ArgumentNullException.ThrowIfNull(interopImports);

        var referenceParameters = method.Signature.ParameterSignatureTypes
            .Select((type, index) => (Type: type, Index: index))
            .Where(parameter => InteropTypeClassifier.IsJavaScriptReference(parameter.Type))
            .ToArray();
        // A nullable parameter arrives as two Wasm values, its presence and its value.
        var parameterLocals = InteropTypeClassifier.TransportLocals(method.Signature.ParameterSignatureTypes, 0);
        var temporaryI4 = parameterLocals[^1];
        var objectTemporary = temporaryI4 + 1;
        var referenceLocals = referenceParameters.Select((parameter, index) =>
            (parameter.Index, Local: objectTemporary + 1 + index)).ToDictionary();
        var rootSlots = referenceParameters.Select((parameter, index) =>
            (parameter.Index, Slot: index)).ToDictionary();
        var nextLocal = objectTemporary + 1 + referenceParameters.Length;
        var hasResult = method.Signature.ReturnType != CliValueKind.Void;
        // A JSObject result leaves as its host handle; the host looks the value up.
        var hostObjectResult = InteropTypeClassifier.IsJSObject(method.Signature.ReturnSignatureType);
        // An object, exception or array of references leaves as a handle the host releases.
        var handleValueResult = InteropTypeClassifier.IsHandleValue(method.Signature.ReturnSignatureType);
        var converted = valueFramePlanner.Plan(method.Signature.ParameterSignatureTypes, method.Signature.ReturnSignatureType);
        var resultKind = hostObjectResult || handleValueResult
            ? CliValueKind.I4
            : converted?.Result is { } convertedResult
                ? InteropTypeClassifier.ConvertedResultKind(convertedResult.Type)
                : method.Signature.ReturnType;
        var rootResult = resultKind == CliValueKind.ManagedReference;
        var resultRootSlot = referenceParameters.Length;
        var rootSlotCount = referenceParameters.Length + (rootResult ? 1 : 0);
        var resultLocal = nextLocal;
        var exceptionLocal = resultLocal + (hasResult ? 1 : 0);
        var rootFrameLocal = exceptionLocal + 1;
        var typeIdLocal = rootFrameLocal + 1;
        var messageLocal = typeIdLocal + 1;
        var messageLengthLocal = messageLocal + 1;
        var stackTraceLocal = messageLengthLocal + 1;
        var stackTraceLengthLocal = stackTraceLocal + 1;
        var valueFrameLocal = stackTraceLengthLocal + 1;
        var code = writers.Create();
        WasmLocalDeclarationWriter.Write(
            code.Bytes,
            [CliValueKind.I4, CliValueKind.ManagedReference,
                .. referenceParameters.Select(_ => CliValueKind.ManagedReference),
                .. (hasResult ? [resultKind] : Array.Empty<CliValueKind>()),
                CliValueKind.ManagedReference,
                CliValueKind.ManagedAddress,
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                .. (converted is null ? Array.Empty<CliValueKind>() : [CliValueKind.ManagedAddress])],
            layouts.Target);

        void EmitBody()
        {
            runtimeInitialization.Initialize(code, initialization);
            if (rootSlotCount != 0)
            {
                WriteI32Constant(code, rootSlotCount);
                WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.RootFrameEnter));
                WriteLocalSet(code, rootFrameLocal);
            }
            foreach (var parameter in referenceParameters)
            {
                referenceArguments.Emit(
                    code.Instructions,
                    parameter.Type,
                    parameterLocals[parameter.Index],
                    referenceLocals[parameter.Index],
                    temporaryI4,
                    objectTemporary,
                    new InteropMarshallingTarget(interopImports),
                    functionIndices.ManagedThrowFunction);
                WriteRootAddress(code, rootFrameLocal);
                WriteLocalGet(code, referenceLocals[parameter.Index]);
                ManagedMemoryEmitter.EmitStoreBySize(
                    code.Instructions,
                    layouts.Target,
                    rootSlots[parameter.Index] * layouts.Target.ObjectReferenceSize,
                    layouts.Target.ObjectReferenceSize);
            }
            if (converted is not null)
            {
                valueFrames.Emit(
                    code.Instructions, new JavaScriptValueFrameEnter(converted, valueFrameLocal, [.. parameterLocals], interopImports));
                if (converted.Result is { } resultSlot)
                    valueFrames.Emit(code.Instructions, new JavaScriptValueFrameAddress(valueFrameLocal, resultSlot.Offset));
            }
            for (var index = 0; index < method.Signature.ParameterSignatureTypes.Length; index++)
            {
                if (rootSlots.TryGetValue(index, out var slot))
                {
                    WriteRootAddress(code, rootFrameLocal);
                    ManagedMemoryEmitter.EmitLoadBySize(
                        code.Instructions,
                        layouts.Target,
                        slot * layouts.Target.ObjectReferenceSize,
                        layouts.Target.ObjectReferenceSize);
                }
                else if (converted?.Parameters.TryGetValue(index, out var valueSlot) == true)
                    valueFrames.Emit(code.Instructions, new JavaScriptValueFrameAddress(valueFrameLocal, valueSlot.Offset));
                else WriteLocalGet(code, parameterLocals[index]);
            }
            WriteCall(code, functionIndices.Resolve(method.Key));
            if (hostObjectResult)
            {
                WriteLocalSet(code, objectTemporary);
                EmitHostObjectHandle(code, objectTemporary);
                WriteLocalSet(code, resultLocal);
            }
            else if (handleValueResult)
            {
                WriteCall(code, interopImports.FromManaged(method.Signature.ReturnSignatureType));
                WriteLocalSet(code, resultLocal);
            }
            else if (converted?.Result is not null)
            {
                valueFrames.Emit(code.Instructions, new JavaScriptValueFrameResult(converted, valueFrameLocal, interopImports));
                WriteLocalSet(code, resultLocal);
            }
            else if (hasResult)
                WriteLocalSet(code, resultLocal);
            // A nullable result's record stays where it is until the host reads it on return.
            if (converted is not null)
                valueFrames.Emit(code.Instructions, new JavaScriptValueFrameLeave(valueFrameLocal));
            if (rootResult)
            {
                WriteRootAddress(code, rootFrameLocal);
                WriteLocalGet(code, resultLocal);
                ManagedMemoryEmitter.EmitStoreBySize(
                    code.Instructions,
                    layouts.Target,
                    resultRootSlot * layouts.Target.ObjectReferenceSize,
                    layouts.Target.ObjectReferenceSize);
            }
            if (hasFinalizers)
            {
                WriteCall(code, runtimeImports.Resolve(
                    RuntimeImportSymbol.FinalizerSafepoint,
                    initialization.RuntimeImportSelection));
                WriteLocalSet(code, exceptionLocal);
            }
            if (rootResult)
            {
                WriteRootAddress(code, rootFrameLocal);
                ManagedMemoryEmitter.EmitLoadBySize(
                    code.Instructions,
                    layouts.Target,
                    resultRootSlot * layouts.Target.ObjectReferenceSize,
                    layouts.Target.ObjectReferenceSize);
                WriteLocalSet(code, resultLocal);
            }
            if (rootSlotCount != 0) LeaveRootFrame(code, rootFrameLocal);
            if (hasFinalizers)
            {
                WriteLocalGet(code, exceptionLocal);
                code.Instructions.Write(WasmInstruction.NoOperand(
                    layouts.Target.UsesMemory64
                        ? WasmOpcodes.I64EqualZero
                        : WasmOpcodes.I32EqualZero));
                code.Instructions.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.If,
                    WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
                code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
                WriteLocalGet(code, exceptionLocal);
                code.Instructions.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.Throw,
                    WasmInstructionOperand.Unsigned(0)));
                code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            }
        }

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
                EmitBody);
        }
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return code.Snapshots.Read();
    }

    private void EmitHostObjectHandle(GeneratedFunctionWriterLease code, int objectLocal)
    {
        WriteLocalGet(code, objectLocal);
        addresses.Emit(code.Instructions, AddressOperation.EqualZero);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType((byte)WasmValueType.I32)));
        WriteI32Constant(code, 0);
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        WriteLocalGet(code, objectLocal);
        addresses.Emit(code.Instructions, layouts.Target.ObjectHeaderSize);
        addresses.Emit(code.Instructions, AddressOperation.Add);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, 0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private static void WriteLocalGet(GeneratedFunctionWriterLease code, int index) =>
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)index)));

    private static void WriteLocalSet(GeneratedFunctionWriterLease code, int index) =>
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)index)));

    private static void WriteCall(GeneratedFunctionWriterLease code, int index) =>
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)index)));

    private static void WriteI32Constant(GeneratedFunctionWriterLease code, int value) =>
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));

    private static void WriteRootAddress(
        GeneratedFunctionWriterLease code,
        int rootFrameLocal) => WriteLocalGet(code, rootFrameLocal);

    private void LeaveRootFrame(GeneratedFunctionWriterLease code, int rootFrameLocal)
    {
        WriteLocalGet(code, rootFrameLocal);
        WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.RootFrameLeave));
    }
}
