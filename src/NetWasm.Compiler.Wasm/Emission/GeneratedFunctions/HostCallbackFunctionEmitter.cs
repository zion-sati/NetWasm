using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

// A JSImport callback is entered the way a synchronous export is: string,
// array and JSObject arguments arrive as host handles and become managed objects
// in a root frame for the call, and a JSObject result leaves as its host handle.
// The first parameter is the delegate's own handle.
internal sealed class HostCallbackFunctionEmitter(
    ITargetLayout layouts,
    IRuntimeImportResolver runtimeImports,
    IRuntimeStateInitializer runtimeInitialization,
    IImplicitExceptionEmitter exceptions,
    IJavaScriptReferenceArgumentEmitter referenceArguments,
    IJavaScriptValueFramePlanner valueFramePlanner,
    IJavaScriptValueFrameEmitter valueFrames,
    IManagedTerminalExceptionBoundaryEmitter terminalExceptions,
    IAddressInstructionEmitter addresses,
    IGeneratedFunctionWriterFactory writers) : IHostCallbackFunctionEmitter
{
    public byte[] Emit(
        HostCallbackDeclaration callback,
        FunctionIndexMap functionIndices,
        RuntimeInitializationPlan initialization,
        InteropImportPlan interopImports,
        RuntimeImportSelection runtimeImportSelection)
    {
        var invoke = callback.Invoke;
        var code = writers.Create();
        var referenceParameters = invoke.Signature.ParameterSignatureTypes
            .Select((type, index) => (Type: type, Index: index))
            .Where(parameter => InteropTypeClassifier.IsJavaScriptReference(parameter.Type))
            .ToArray();
        var rootSlots = referenceParameters.Select((parameter, slot) =>
            (parameter.Index, Slot: slot)).ToDictionary();
        // The delegate's handle comes first; a nullable parameter then takes two Wasm values.
        var parameterLocals = InteropTypeClassifier.TransportLocals(invoke.Signature.ParameterSignatureTypes, 1);
        var delegateLocal = parameterLocals[^1];
        var temporaryI4 = delegateLocal + 1;
        var objectTemporary = temporaryI4 + 1;
        var referenceLocals = referenceParameters.Select((parameter, index) =>
            (parameter.Index, Local: objectTemporary + 1 + index)).ToDictionary();
        var nextLocal = objectTemporary + 1 + referenceParameters.Length;
        var hasResult = invoke.Signature.ReturnType != CliValueKind.Void;
        var hostObjectResult = InteropTypeClassifier.IsJSObject(invoke.Signature.ReturnSignatureType);
        var handleValueResult = InteropTypeClassifier.IsHandleValue(invoke.Signature.ReturnSignatureType);
        var converted = valueFramePlanner.Plan(invoke.Signature.ParameterSignatureTypes, invoke.Signature.ReturnSignatureType);
        var resultKind = hostObjectResult || handleValueResult
            ? CliValueKind.I4
            : converted?.Result is { } convertedResult
                ? InteropTypeClassifier.ConvertedResultKind(convertedResult.Type)
                : invoke.Signature.ReturnType;
        var resultLocal = nextLocal;
        var exceptionLocal = resultLocal + (hasResult ? 1 : 0);
        var rootFrameLocal = exceptionLocal + 1;
        var typeIdLocal = rootFrameLocal + 1;
        var messageLocal = typeIdLocal + 1;
        var messageLengthLocal = messageLocal + 1;
        var stackTraceLocal = messageLengthLocal + 1;
        var stackTraceLengthLocal = stackTraceLocal + 1;
        var valueFrameLocal = stackTraceLengthLocal + 1;
        WasmLocalDeclarationWriter.Write(
            code.Bytes,
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.ManagedReference,
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
                runtimeImportSelection),
            runtimeImports.Resolve(
                RuntimeImportSymbol.ManagedTerminalExceptionRaise,
                runtimeImportSelection),
            () =>
            {
                runtimeInitialization.Initialize(code, initialization);
                WriteLocalGet(code, 0);
                WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.HandleGet));
                WriteLocalSet(code, delegateLocal);
                WriteLocalGet(code, delegateLocal);
                addresses.Emit(code.Instructions, AddressOperation.EqualZero);
                code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
                exceptions.Emit(code.Instructions, ManagedExceptionKind.JSException, functionIndices.ManagedThrowFunctionIndex);
                code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
                // Each converted argument stays rooted while the next allocates.
                if (referenceParameters.Length != 0)
                {
                    WriteI32Constant(code, referenceParameters.Length);
                    WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.RootFrameEnter));
                    WriteLocalSet(code, rootFrameLocal);
                }
                var target = new InteropMarshallingTarget(interopImports);
                foreach (var parameter in referenceParameters)
                {
                    referenceArguments.Emit(code.Instructions, parameter.Type, parameterLocals[parameter.Index],
                        referenceLocals[parameter.Index], temporaryI4, objectTemporary, target,
                        functionIndices.ManagedThrowFunctionIndex);
                    WriteLocalGet(code, rootFrameLocal);
                    WriteLocalGet(code, referenceLocals[parameter.Index]);
                    ManagedMemoryEmitter.EmitStoreBySize(code.Instructions, layouts.Target,
                        rootSlots[parameter.Index] * layouts.Target.ObjectReferenceSize,
                        layouts.Target.ObjectReferenceSize);
                }
                // The invoke helper takes a value-type result's address first, then the delegate.
                if (converted is not null)
                {
                    valueFrames.Emit(
                        code.Instructions, new JavaScriptValueFrameEnter(converted, valueFrameLocal, [.. parameterLocals], interopImports));
                    if (converted.Result is { } resultSlot)
                        valueFrames.Emit(code.Instructions, new JavaScriptValueFrameAddress(valueFrameLocal, resultSlot.Offset));
                }
                WriteLocalGet(code, delegateLocal);
                for (var index = 0; index < invoke.Signature.ParameterSignatureTypes.Length; index++)
                {
                    if (rootSlots.TryGetValue(index, out var slot))
                    {
                        WriteLocalGet(code, rootFrameLocal);
                        ManagedMemoryEmitter.EmitLoadBySize(code.Instructions, layouts.Target,
                            slot * layouts.Target.ObjectReferenceSize, layouts.Target.ObjectReferenceSize);
                    }
                    else if (converted?.Parameters.TryGetValue(index, out var valueSlot) == true)
                        valueFrames.Emit(code.Instructions, new JavaScriptValueFrameAddress(valueFrameLocal, valueSlot.Offset));
                    else WriteLocalGet(code, parameterLocals[index]);
                }
                WriteCall(code, functionIndices.GetDelegateInvokeHelper(invoke.DeclaringType.CanonicalName).Value);
                if (hostObjectResult)
                {
                    WriteLocalSet(code, objectTemporary);
                    EmitHostObjectHandle(code, objectTemporary);
                }
                else if (handleValueResult)
                {
                    WriteCall(code, interopImports.FromManaged(invoke.Signature.ReturnSignatureType));
                }
                else if (converted?.Result is not null)
                {
                    valueFrames.Emit(code.Instructions, new JavaScriptValueFrameResult(converted, valueFrameLocal, interopImports));
                }
                if (hasResult)
                {
                    WriteLocalSet(code, resultLocal);
                }
                // A nullable result's record stays where it is until the host reads it on return.
                if (converted is not null)
                    valueFrames.Emit(code.Instructions, new JavaScriptValueFrameLeave(valueFrameLocal));
                if (referenceParameters.Length != 0)
                {
                    WriteLocalGet(code, rootFrameLocal);
                    WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.RootFrameLeave));
                }
            });
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
}
