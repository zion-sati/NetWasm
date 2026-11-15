// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

using NetWasm.Compiler.Wasm.Emission.Instructions.Calls.ManagedCallSites;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal sealed class CallInstructionEmitter(
    IManagedCallSiteResolver callSites,
    ICallEmissionKindResolver kinds,
    ICallEmissionRegistry emitters,
    IAddressTakenScalarSynchronizationEmitter addressTakenScalarSynchronization,
    IAddressTakenAggregateSynchronizationEmitter addressTakenAggregateSynchronization) :
    InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        Command(CilOperation.Call),
        Command(CilOperation.CallVirtual),
    ];

    private InstructionCommand Command(CilOperation operation) => new(
        operation,
        InstructionFamily.CallsAndCallableLoading,
        EmitCall);

    private void EmitCall(
        InstructionEmissionRequest instruction, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
    {
        var callSite = callSites.Resolve(instruction);
        var consumed = callSite.Target.Signature.ParameterTypes.Length +
                       (callSite.Target.Definition.IsStatic ? 0 : 1);
        var request = new CallEmissionRequest(
            instruction,
            callSite.Target,
            instruction.Stack.Count - consumed,
            consumed,
            callSite.ConstrainedType);
        instruction.Context.AddressTakenScalarPromotions.Plan.CallSynchronizations
            .TryGetValue(instruction.Instruction.Offset, out var scalarSynchronization);
        instruction.Context.AddressTakenAggregatePromotions.Plan.CallSynchronizations
            .TryGetValue(instruction.Instruction.Offset, out var aggregateSynchronization);
        if (scalarSynchronization is not null)
        {
            addressTakenScalarSynchronization.Emit(
                instruction,
                code,
                scalarSynchronization,
                AddressTakenValueSynchronizationPhase.BeforeCall);
        }
        if (aggregateSynchronization is not null)
        {
            addressTakenAggregateSynchronization.Emit(
                instruction,
                code,
                aggregateSynchronization,
                AddressTakenValueSynchronizationPhase.BeforeCall);
        }
        emitters.Get(kinds.Resolve(request, code)).Emit(request, code, functionIndices);
        if (aggregateSynchronization is not null)
        {
            addressTakenAggregateSynchronization.Emit(
                instruction,
                code,
                aggregateSynchronization,
                AddressTakenValueSynchronizationPhase.AfterCall);
        }
        if (scalarSynchronization is not null)
        {
            addressTakenScalarSynchronization.Emit(
                instruction,
                code,
                scalarSynchronization,
                AddressTakenValueSynchronizationPhase.AfterCall);
        }
    }

}
