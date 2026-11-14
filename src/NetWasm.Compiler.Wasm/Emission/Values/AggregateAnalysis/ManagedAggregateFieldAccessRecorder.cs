using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal sealed class ManagedAggregateFieldAccessRecorder(
    IManagedAggregateFieldResolver fields) : IManagedAggregateFieldAccessRecorder
{
    private readonly IManagedAggregateFieldResolver _fields = fields ??
        throw new ArgumentNullException(nameof(fields));

    public void Record(
        CilInstruction instruction,
        StructuredBlockId block,
        ManagedAggregateAddressOrigins origins,
        bool writes,
        ManagedAggregateAnalysisSession session)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(session);
        if (!session.TrySingleRecipe(origins, out var recipe))
        {
            return;
        }
        if (!_fields.TryResolve(recipe.Type, instruction.Operand,
                out var field, out var type))
        {
            session.Reject(recipe.Storage);
            return;
        }
        session.RecordAccess(
            instruction,
            block,
            recipe with
            {
                ByteOffset = recipe.ByteOffset + field.Offset,
                Type = type,
            },
            field.Size,
            type.StackKind,
            writes);
    }
}
