// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

// A managed calli calls the static method of its signature whose address the pointer holds, found by searching the
// methods' addresses; a pointer to no such method throws InvalidCastException.
internal sealed class CallIndirectEmitter(
    ITargetLayout layouts,
    IKeyedDispatchEmitter keyedDispatch,
    IImplicitExceptionEmitter exceptions,
    IValueFrameAddressEmitter valueFrames,
    INativeAbiCallEmitter nativeCalls) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        new InstructionCommand(
            CilOperation.CallIndirect,
            InstructionFamily.CallsAndCallableLoading,
            EmitCallIndirect),
    ];

    private void EmitCallIndirect(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
    {
        var callSite = request.Instruction.Operand as CilOperand.CallSite ??
            throw new InvalidOperationException("calli has no call-site signature");
        var signature = callSite.Signature;
        if (callSite.Unmanaged)
        {
            // Native code is called through its pointer by the trampoline for the call's C ABI
            // signature, the arguments lowered as a DllImport's (issue 226).
            var native = request.Target.NativeImports.CallSites.For(signature);
            nativeCalls.Emit(new(
                request,
                native.Lowering,
                request.Stack.Count - signature.ParameterTypes.Length - 1,
                signature.ParameterTypes.Length + 1,
                HostCall: false,
                request.Header.MethodInstance?.CanonicalName ?? request.Header.Method.Name)
            {
                Trampoline = native.Trampoline,
            }, code, functionIndices);
            return;
        }
        // A value-type result goes to the call's frame buffer, whose address the managed ABI passes
        // first and the evaluation stack then holds.
        var returnsValue = signature.ReturnSignatureType.StackKind == CliValueKind.ValueType;
        var returnOffset = returnsValue
            ? request.Context.ValueLayout.TemporaryOffsets[request.Instruction.Offset]
            : 0;
        var argumentBase = request.Stack.Count - signature.ParameterTypes.Length - 1;
        var functionSlot = argumentBase + signature.ParameterTypes.Length;
        var compatible = request.Target.CallableMethods.Read().Values
            .Where(target => target.Definition.IsStatic &&
                DelegateSignatureComparer.AreEqual(target.Signature, signature))
            .Select(target => (Address: functionIndices.Resolve(new FunctionAddress(target)), Target: target))
            .DistinctBy(entry => entry.Address)
            .OrderBy(entry => entry.Address)
            .ToArray();
        keyedDispatch.Emit(code, new(
            GetStackLocal(request.Context, functionSlot, CliValueKind.NativeInt),
            layouts.Target.UsesMemory64,
            [.. compatible.Select(entry => new KeyRange(entry.Address, entry.Address))],
            index => EmitTargetCall(compatible[index].Target),
            () => exceptions.Emit(code, ManagedExceptionKind.InvalidCast, request.Context.ManagedThrowFunction)));

        void EmitTargetCall(MethodInstanceModel target)
        {
            if (returnsValue)
            {
                valueFrames.Emit(code, request.Context, returnOffset);
            }
            for (var index = 0; index < signature.ParameterTypes.Length; index++)
            {
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                    request.Context,
                    argumentBase + index,
                    request.Stack[argumentBase + index])))));
            }
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(functionIndices.Resolve(target)))));
            if (!returnsValue && signature.ReturnType != CliValueKind.Void)
            {
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                    request.Context,
                    argumentBase,
                    signature.ReturnType)))));
            }
        }
        request.Stack.RemoveRange(argumentBase, signature.ParameterTypes.Length + 1);
        if (returnsValue)
        {
            valueFrames.Emit(code, request.Context, returnOffset);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                request.Context,
                argumentBase,
                CliValueKind.ValueType)))));
            request.Stack.Add(CliValueKind.ValueType);
        }
        else if (signature.ReturnType != CliValueKind.Void)
        {
            request.Stack.Add(signature.ReturnType);
        }
    }

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);
}
