// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface ISharedVirtualDispatchFunctionEmitter
{
    byte[] Emit(
        SharedVirtualDispatchPlan dispatch,
        IEntryLookup<string, StaticInitializerGuard> staticInitializerGuards,
        IFunctionIndexResolver functionIndices);
}

// A shared virtual dispatch function takes its declaration's parameters (a value-type result's address first, then
// the receiver and the arguments), finds the receiver's target by a binary search of its type id, and calls it with
// them, as a call site's own dispatch does. The caller has checked the receiver for null. Nothing is rooted here:
// nothing is read once the target runs, as for a delegate's invoke function.
internal sealed class SharedVirtualDispatchFunctionEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IManagedMethodFunctionTypeResolver functionTypes,
    IVirtualDispatchEmissionPlanBuilder plans,
    IVirtualDispatchCasePlanner cases,
    IImplicitExceptionEmitter exceptions,
    IStaticInitializationEmitter initialization,
    IMethodInitializationTriggerClassifier initializationTriggers,
    IGeneratedFunctionWriterFactory writers) : ISharedVirtualDispatchFunctionEmitter
{
    public byte[] Emit(
        SharedVirtualDispatchPlan dispatch,
        IEntryLookup<string, StaticInitializerGuard> staticInitializerGuards,
        IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(staticInitializerGuards);
        ArgumentNullException.ThrowIfNull(functionIndices);
        var declaration = dispatch.Declaration;
        var parameterCount = functionTypes.Resolve(declaration).Parameters.Length;
        var receiver = declaration.Signature.ReturnSignatureType.StackKind == CliValueKind.ValueType ? 1 : 0;
        var typeIdLocal = parameterCount;
        var plan = plans.Build(declaration, dispatch.Targets);
        var dispatchCases = cases.Plan(plan.CheckedTargets, dispatch.Targets);

        var output = writers.Create();
        var code = output.Instructions;
        WasmLocalDeclarationWriter.Write(output.Bytes, [CliValueKind.I4], layouts.Target);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)receiver)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, 0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)typeIdLocal)));
        if (!dispatchCases.IsEmpty)
            EmitSearch(code, dispatchCases, 0, dispatchCases.Length, receiver, parameterCount, typeIdLocal,
                staticInitializerGuards, functionIndices);
        // No case matched: the declaration's own body on object, or no target at all.
        if (plan.FallbackTarget is { } fallback)
            EmitTarget(code, fallback, receiver, parameterCount, staticInitializerGuards, functionIndices);
        else
            exceptions.Emit(code, ManagedExceptionKind.InvalidCast, functionIndices.ManagedThrowFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return output.Snapshots.Read();
    }

    // A balanced search: each leaf tests its run of type ids and returns its target's result; a receiver that matches
    // none falls through.
    private void EmitSearch(
        IWasmInstructionWriter code,
        ImmutableArray<VirtualDispatchCase> dispatchCases,
        int start,
        int count,
        int receiver,
        int parameterCount,
        int typeIdLocal,
        IEntryLookup<string, StaticInitializerGuard> guards,
        IFunctionIndexResolver functionIndices)
    {
        if (count == 1)
        {
            var dispatchCase = dispatchCases[start];
            if (dispatchCase.LowTypeId == dispatchCase.HighTypeId)
            {
                EmitComparison(code, typeIdLocal, dispatchCase.LowTypeId, WasmOpcodes.I32Equal);
            }
            else
            {
                EmitComparison(code, typeIdLocal, dispatchCase.LowTypeId, WasmOpcodes.I32Subtract);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
                    WasmInstructionOperand.Signed(dispatchCase.HighTypeId - dispatchCase.LowTypeId + 1)));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32LessThanUnsigned));
            }
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            EmitTarget(code, dispatchCase.Target, receiver, parameterCount, guards, functionIndices);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            return;
        }
        var leftCount = count / 2;
        var pivot = start + leftCount;
        EmitComparison(code, typeIdLocal, dispatchCases[pivot].LowTypeId, WasmOpcodes.I32LessThanUnsigned);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitSearch(code, dispatchCases, start, leftCount, receiver, parameterCount, typeIdLocal, guards, functionIndices);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        EmitSearch(code, dispatchCases, pivot, count - leftCount, receiver, parameterCount, typeIdLocal, guards,
            functionIndices);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private static void EmitComparison(IWasmInstructionWriter code, int typeIdLocal, int typeId, byte comparison)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)typeIdLocal)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(typeId)));
        code.Write(WasmInstruction.NoOperand(comparison));
    }

    // The target's call with this function's parameters, the receiver at its payload for a method a value type
    // declares, and a return of its result.
    private void EmitTarget(
        IWasmInstructionWriter code,
        DispatchTargetModel target,
        int receiver,
        int parameterCount,
        IEntryLookup<string, StaticInitializerGuard> guards,
        IFunctionIndexResolver functionIndices)
    {
        if (initializationTriggers.Classify(target.Method.Definition))
        {
            initialization.Emit(new(target.Method.Definition.DeclaringType, target.Method.DeclaringType, guards,
                IsMethodInvocation: true), code, functionIndices);
        }
        for (var parameter = 0; parameter < parameterCount; parameter++)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)parameter)));
            if (parameter == receiver && target.ReceiverType.IsValueType && target.Method.DeclaringType.IsValueType)
            {
                var payloadOffset = WasmTargetLayout.Align(layouts.Target.ObjectHeaderSize,
                    values.GetValueLayout(target.ReceiverType).Alignment);
                if (layouts.Target.UsesMemory64)
                {
                    code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(payloadOffset)));
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Add));
                }
                else
                {
                    code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(payloadOffset)));
                    code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
                }
            }
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(target.Method))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
    }
}
