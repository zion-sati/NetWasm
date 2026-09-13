using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class HostCallbackFunctionEmitter(
    ITargetLayout layouts,
    IRuntimeImportResolver runtimeImports,
    IRuntimeStateInitializer runtimeInitialization,
    IImplicitExceptionEmitter exceptions,
    IHostCallbackStringArgumentMarshaller stringArguments,
    IHostCallbackByteArrayArgumentMarshaller byteArrayArguments,
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
            .Where(parameter => InteropTypeClassifier.IsString(parameter.Type) ||
                                InteropTypeClassifier.IsByteArray(parameter.Type))
            .ToArray();
        var delegateLocal = invoke.Signature.ParameterTypes.Length + 1;
        var temporaryI4 = delegateLocal + 1;
        var objectTemporary = temporaryI4 + 1;
        var referenceLocals = referenceParameters.Select((parameter, index) =>
            (parameter.Index, Local: objectTemporary + 1 + index)).ToDictionary();
        var nextLocal = objectTemporary + 1 + referenceParameters.Length;
        var hasResult = invoke.Signature.ReturnType != CliValueKind.Void;
        var resultLocal = nextLocal;
        var exceptionLocal = resultLocal + (hasResult ? 1 : 0);
        var rootFrameLocal = exceptionLocal + 1;
        var typeIdLocal = rootFrameLocal + 1;
        var messageLocal = typeIdLocal + 1;
        var messageLengthLocal = messageLocal + 1;
        WasmLocalDeclarationWriter.Write(
            code.Bytes,
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.ManagedReference,
                .. referenceParameters.Select(_ => CliValueKind.ManagedReference),
                .. (hasResult ? [invoke.Signature.ReturnType] : Array.Empty<CliValueKind>()),
                CliValueKind.ManagedReference,
                CliValueKind.ManagedAddress,
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.I4],
            layouts.Target);
        terminalExceptions.Emit(
            code.Instructions,
            exceptionLocal,
            rootFrameLocal,
            typeIdLocal,
            messageLocal,
            messageLengthLocal,
            invoke.Signature.ReturnType,
            resultLocal,
            runtimeImports.Resolve(
                RuntimeImportSymbol.ManagedTerminalExceptionReport,
                runtimeImportSelection),
            () =>
            {
                runtimeInitialization.Initialize(code, initialization);
                code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned(0)));
                code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
                    WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                        RuntimeImportSymbol.HandleGet))));
                code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned((uint)delegateLocal)));
                code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)delegateLocal)));
                addresses.Emit(code.Instructions, AddressOperation.EqualZero);
                code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
                exceptions.Emit(code.Instructions, ManagedExceptionKind.JSException);
                code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
                code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)delegateLocal)));
                for (var index = 0; index < invoke.Signature.ParameterSignatureTypes.Length; index++)
                {
                    var type = invoke.Signature.ParameterSignatureTypes[index];
                    if (InteropTypeClassifier.IsString(type))
                    {
                        stringArguments.Emit(
                            code.Instructions,
                            index + 1,
                            referenceLocals[index],
                            temporaryI4,
                            objectTemporary,
                            new InteropMarshallingTarget(interopImports));
                        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                            WasmInstructionOperand.Unsigned((uint)referenceLocals[index])));
                    }
                    else if (InteropTypeClassifier.IsByteArray(type))
                    {
                        byteArrayArguments.Emit(
                            code.Instructions,
                            type,
                            index + 1,
                            referenceLocals[index],
                            temporaryI4,
                            objectTemporary,
                            new InteropMarshallingTarget(interopImports));
                        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                            WasmInstructionOperand.Unsigned((uint)referenceLocals[index])));
                    }
                    else
                    {
                        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                            WasmInstructionOperand.Unsigned((uint)(index + 1))));
                    }
                }
                code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
                    WasmInstructionOperand.Unsigned((uint)functionIndices.GetDelegateInvokeHelper(
                        invoke.DeclaringType.CanonicalName).Value)));
                if (hasResult)
                {
                    code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                        WasmInstructionOperand.Unsigned((uint)resultLocal)));
                }
            });
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return code.Snapshots.Read();
    }
}
