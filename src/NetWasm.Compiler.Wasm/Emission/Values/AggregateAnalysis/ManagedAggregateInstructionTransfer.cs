using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal sealed class ManagedAggregateInstructionTransfer(
    IArgumentSignatureTypeResolver argumentTypes,
    IManagedAggregateFieldAddressResolver fieldAddresses,
    IManagedAggregateFieldAccessRecorder fieldAccesses,
    IManagedAggregateObjectAccessRecorder objectAccesses,
    IManagedAggregateClearRecorder clears,
    IManagedAggregateCallEffectApplier calls) : IManagedAggregateInstructionTransfer
{
    private readonly IArgumentSignatureTypeResolver _argumentTypes = argumentTypes ??
        throw new ArgumentNullException(nameof(argumentTypes));
    private readonly IManagedAggregateFieldAddressResolver _fieldAddresses = fieldAddresses ??
        throw new ArgumentNullException(nameof(fieldAddresses));
    private readonly IManagedAggregateFieldAccessRecorder _fieldAccesses = fieldAccesses ??
        throw new ArgumentNullException(nameof(fieldAccesses));
    private readonly IManagedAggregateObjectAccessRecorder _objectAccesses = objectAccesses ??
        throw new ArgumentNullException(nameof(objectAccesses));
    private readonly IManagedAggregateClearRecorder _clears = clears ??
        throw new ArgumentNullException(nameof(clears));
    private readonly IManagedAggregateCallEffectApplier _calls = calls ??
        throw new ArgumentNullException(nameof(calls));

    public void Apply(
        ManagedAggregateInstructionStep step,
        List<ManagedAggregateAddressOrigins> stack,
        ImmutableArray<ManagedAggregateAddressOrigins>.Builder locals,
        ManagedAggregateAnalysisSession session)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(locals);
        ArgumentNullException.ThrowIfNull(session);
        var request = session.Request;
        var instruction = step.Instruction;
        var blockId = step.Block;
        var consumed = step.Consumed;
        var producedOrigins = ManagedAggregateAddressOrigins.None;
        switch (instruction.Operation)
        {
            case CilOperation.LoadArgumentAddress:
                producedOrigins = session.RootOrigins(
                    ManagedAggregateStorageKind.Argument,
                    CilOperandReader.GetIndex(instruction));
                break;
            case CilOperation.LoadLocalAddress:
                producedOrigins = session.RootOrigins(
                    ManagedAggregateStorageKind.Local,
                    CilOperandReader.GetIndex(instruction));
                break;
            case CilOperation.LoadArgument:
                {
                    var argument = CilOperandReader.GetIndex(instruction);
                    var type = _argumentTypes.Resolve(request.Method.Header, argument);
                    if (type.StackKind == CliValueKind.ManagedAddress)
                    {
                        producedOrigins = ManagedAggregateAddressOrigins.UnknownOnly;
                    }
                    else
                    {
                        session.RecordWholeValueMaterialization(
                            instruction.Offset,
                            ManagedAggregateStorageKind.Argument,
                            argument);
                    }
                    break;
                }
            case CilOperation.LoadLocal:
                {
                    var local = CilOperandReader.GetIndex(instruction);
                    if (request.Method.Header.LocalSignatureTypes[local].StackKind ==
                        CliValueKind.ManagedAddress)
                    {
                        producedOrigins = locals[local];
                    }
                    else
                    {
                        session.RecordWholeValueMaterialization(
                            instruction.Offset,
                            ManagedAggregateStorageKind.Local,
                            local);
                    }
                    break;
                }
            case CilOperation.StoreLocal:
                {
                    var local = CilOperandReader.GetIndex(instruction);
                    if (request.Method.Header.LocalSignatureTypes[local].StackKind ==
                        CliValueKind.ManagedAddress)
                    {
                        locals[local] = stack[^1];
                    }
                    RejectStoredAddress(stack[^1],
                        request.Method.Header.LocalSignatureTypes[local].StackKind !=
                            CliValueKind.ManagedAddress,
                        session);
                    session.RecordWholeValueStore(
                        instruction.Offset,
                        blockId,
                        ManagedAggregateStorageKind.Local,
                        local);
                    break;
                }
            case CilOperation.Duplicate:
                stack.Add(stack[^1]);
                return;
            case CilOperation.LoadFieldAddress:
                producedOrigins = _fieldAddresses.Resolve(
                    instruction,
                    stack[^1],
                    session);
                break;
            case CilOperation.LoadField:
                _fieldAccesses.Record(
                    instruction,
                    blockId,
                    stack[^1],
                    writes: false,
                    session);
                break;
            case CilOperation.StoreField:
                _fieldAccesses.Record(
                    instruction,
                    blockId,
                    stack[^2],
                    writes: true,
                    session);
                session.Reject(stack[^1]);
                break;
            case CilOperation.LoadObject:
                _objectAccesses.Record(
                    instruction,
                    blockId,
                    stack[^1],
                    writes: false,
                    session);
                break;
            case CilOperation.StoreObject:
                _objectAccesses.Record(
                    instruction,
                    blockId,
                    stack[^2],
                    writes: true,
                    session);
                session.Reject(stack[^1]);
                break;
            case CilOperation.InitializeObject when request.References:
                _clears.Record(
                    instruction,
                    blockId,
                    stack[^1],
                    session);
                break;
            case CilOperation.Return:
            case CilOperation.StoreStaticField:
            case CilOperation.StoreArrayElement:
            case CilOperation.StoreArrayElementReference:
            case CilOperation.StoreRectangularArrayElement:
            case CilOperation.CallIndirect:
            case CilOperation.CopyObject:
            case CilOperation.InitializeObject:
            case CilOperation.CopyBlock:
            case CilOperation.InitializeBlock:
            case CilOperation.CompareExchange:
                session.RejectConsumed(stack, consumed);
                break;
            case CilOperation.StoreArgument:
                session.RejectConsumed(stack, consumed);
                session.RecordWholeValueStore(
                    instruction.Offset,
                    blockId,
                    ManagedAggregateStorageKind.Argument,
                    CilOperandReader.GetIndex(instruction));
                break;
            case CilOperation.Call:
                var recorded = session.Materializations.Count;
                producedOrigins = _calls.Apply(
                    instruction,
                    stack,
                    consumed,
                    session);
                // A call that writes the storage flushes and reloads every lane, as a
                // whole-value store does, and pays off only where a store would.
                for (var added = recorded; added < session.Materializations.Count; added++)
                {
                    if (session.Materializations[added].ReloadAfter)
                    {
                        session.WholeValueStores.Add((blockId, session.Materializations[added].Storage));
                    }
                }
                break;
            case CilOperation.CallVirtual:
            case CilOperation.NewObject:
                session.RejectConsumed(stack, consumed);
                break;
            default:
                if (ProducesManagedAddress(step.OutgoingKinds, step.Produced))
                {
                    session.RejectConsumed(stack, consumed);
                    producedOrigins = ManagedAggregateAddressOrigins.UnknownOnly;
                }
                else if (!IsAddressTransparent(instruction.Operation))
                {
                    session.RejectConsumed(stack, consumed);
                }
                break;
        }

        if (ProducesManagedAddress(step.OutgoingKinds, step.Produced) &&
            !producedOrigins.Unknown &&
            producedOrigins.Recipes.IsEmpty)
        {
            producedOrigins = ManagedAggregateAddressOrigins.UnknownOnly;
        }

        stack.RemoveRange(stack.Count - consumed, consumed);
        if (step.Produced != 0)
        {
            stack.Add(producedOrigins);
        }
    }

    private static bool ProducesManagedAddress(
        ImmutableArray<CliValueKind> outgoing,
        int produced) => produced != 0 &&
        !outgoing.IsEmpty &&
        outgoing[^1] == CliValueKind.ManagedAddress;

    private static bool IsAddressTransparent(CilOperation operation) => operation is
        CilOperation.Nop or CilOperation.Branch or CilOperation.Break or
        CilOperation.Constrained or CilOperation.Volatile or
        CilOperation.Unaligned or CilOperation.Readonly;

    private static void RejectStoredAddress(
        ManagedAggregateAddressOrigins origins,
        bool storedAsNonAddress,
        ManagedAggregateAnalysisSession session)
    {
        if (storedAsNonAddress)
        {
            session.Reject(origins);
        }
    }
}
