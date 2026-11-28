// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Planning;

using NetWasm.Compiler.Wasm.Emission.StructResults;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class AsyncJSImportResolveEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider valueLayouts,
    IRuntimeImportResolver runtimeImports,
    IAsyncImportTaskHandleEmitter taskHandles,
    IGeneratedFunctionWriterFactory writers,
    IHostCallbackStringArgumentMarshaller stringResults,
    IStagedValueArrayEmitter valueArrayResults,
    IHostCallbackHostObjectArgumentMarshaller hostObjectResults,
    IJavaScriptNullableEmitter nullables,
    IValueTypeArgumentConventionFactory argumentConventions) : IAsyncJSImportResolveEmitter
{
    public byte[] Emit(
        JavaScriptAsyncMethodBinding binding,
        IFunctionIndexResolver functionIndices,
        InteropImportPlan interopImports)
    {
        var code = writers.Create();
        var result = binding.Return.ResultType;
        // A string, array, or JSObject result arrives as a host handle in the
        // second parameter and becomes the managed value passed to SetResult.
        var referenceResult = result is not null && InteropTypeClassifier.IsJavaScriptReference(result);
        // A date result arrives as Unix milliseconds and a nullable one as its presence
        // and value. Either becomes its struct in a value frame, which SetResult receives.
        var convertedResult = result is not null && InteropTypeClassifier.IsConvertedValue(result);
        var taskLocal = result is null ? 1
            : InteropTypeClassifier.IsNullable(result) || InteropTypeClassifier.IsValueArray(result) ? 3 : 2;
        var valueLocal = taskLocal + 1;
        var temporaryI4 = valueLocal + 1;
        var objectTemporary = temporaryI4 + 1;
        WasmLocalDeclarationWriter.Write(
            code.Bytes,
            [
                CliValueKind.ManagedReference,
                .. (referenceResult
                    ? [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.ManagedReference]
                    : convertedResult ? [CliValueKind.ManagedAddress] : Array.Empty<CliValueKind>()),
            ],
            layouts.Target);
        taskHandles.Emit(code.Instructions, 0, taskLocal, functionIndices.ManagedThrowFunction);
        if (referenceResult)
        {
            var target = new InteropMarshallingTarget(interopImports);
            if (InteropTypeClassifier.IsString(result!))
            {
                stringResults.Emit(code.Instructions, 1, valueLocal, temporaryI4, objectTemporary, target, functionIndices.ManagedThrowFunction);
            }
            else if (InteropTypeClassifier.IsValueArray(result!))
            {
                valueArrayResults.Emit(code.Instructions, result!, 1, 2, valueLocal, objectTemporary,
                    functionIndices.ManagedThrowFunction);
            }
            else if (InteropTypeClassifier.IsHandleValue(result!))
            {
                // CoreLib converts the settled value; the host releases its handle.
                WriteLocal(code, WasmOpcodes.LocalGet, 1);
                WriteCall(code, interopImports.ToManaged(result!));
                WriteLocal(code, WasmOpcodes.LocalSet, valueLocal);
            }
            else
            {
                hostObjectResults.Emit(code.Instructions, result!, 1, valueLocal, functionIndices.ManagedThrowFunction);
            }
        }
        if (convertedResult)
        {
            ManagedMemoryEmitter.EmitAddressConstant(
                code.Instructions, layouts.Target, valueLayouts.GetValueLayout(result!).Size);
            WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameEnter));
            WriteLocal(code, WasmOpcodes.LocalSet, valueLocal);
            if (InteropTypeClassifier.IsNullable(result!))
            {
                nullables.Emit(
                    code.Instructions,
                    new JavaScriptNullableFromPair(result!, new(valueLocal), HasValueLocal: 1),
                    interopImports);
            }
            else
            {
                WriteLocal(code, WasmOpcodes.LocalGet, valueLocal);
                WriteLocal(code, WasmOpcodes.LocalGet, 1);
                WriteCall(code, interopImports.ToManaged(result!));
            }
        }
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)taskLocal)));
        if (result is not null)
        {
            // A converted result (a date, a nullable) is a struct in the value frame, passed as SetResult's parameter
            // takes it: its address or its fields.
            var value = referenceResult || convertedResult ? valueLocal : 1;
            argumentConventions.Create(binding.SetResult.Signature.ParameterSignatureTypes[0]).Emit(code.Instructions,
                writer => writer.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)value))),
                new());
        }
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(
                binding.SetResult))));
        if (convertedResult)
        {
            WriteLocal(code, WasmOpcodes.LocalGet, valueLocal);
            WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameLeave));
        }
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.HandleRelease))));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return code.Snapshots.Read();
    }

    private static void WriteLocal(GeneratedFunctionWriterLease code, byte opcode, int local) =>
        code.Instructions.Write(WasmInstruction.WithOperand(opcode, WasmInstructionOperand.Unsigned((uint)local)));

    private static void WriteCall(GeneratedFunctionWriterLease code, int function) =>
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)function)));
}
