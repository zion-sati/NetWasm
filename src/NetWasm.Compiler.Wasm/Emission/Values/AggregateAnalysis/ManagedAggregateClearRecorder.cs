using System;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

// initobj of the whole aggregate writes zero to every lane.
internal sealed class ManagedAggregateClearRecorder(
    ICilTypeOperandResolver typeOperands,
    IValueLayoutProvider values,
    ITargetLayout layouts) : IManagedAggregateClearRecorder
{
    private readonly ICilTypeOperandResolver _typeOperands = typeOperands ??
        throw new ArgumentNullException(nameof(typeOperands));
    private readonly IValueLayoutProvider _values = values ??
        throw new ArgumentNullException(nameof(values));
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));

    public void Record(
        CilInstruction instruction,
        StructuredBlockId block,
        ManagedAggregateAddressOrigins origins,
        ManagedAggregateAnalysisSession session)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(session);
        if (!session.TrySingleRecipe(origins, out var recipe))
        {
            return;
        }
        var storage = session.Storages[recipe.Storage];
        var type = _typeOperands.Resolve(instruction, session.Request.Method.Header.MethodInstance);
        if (!recipe.Type.Equals(type))
        {
            session.Reject(recipe.Storage);
            return;
        }
        // initobj of the whole value, or of a field such as an enumerator's
        // current element, zeroes the lanes its bytes cover; a field narrower
        // than a lane would need a partial lane write. A value's bytes are its
        // layout's, which for a short are two where its stack slot is four.
        var size = recipe.ByteOffset == 0 && type.Equals(storage.Type)
            ? storage.Size
            : type.IsValueType
                ? _values.GetValueLayout(type).Size
                : _layouts.Target.GetStorageSize(type);
        var end = recipe.ByteOffset + size;
        var lanes = storage.Lanes
            .Where(lane => lane.ByteOffset < end && lane.ByteOffset + lane.Size > recipe.ByteOffset)
            .ToArray();
        if (lanes[0].ByteOffset != recipe.ByteOffset ||
            lanes[^1].ByteOffset + lanes[^1].Size != end)
        {
            session.Reject(recipe.Storage);
            return;
        }
        session.Accesses[instruction.Offset] = new(instruction.Offset, block, recipe, size, CliValueKind.ValueType, Writes: true)
        {
            LaneOffsets = [.. lanes.Select(static lane => lane.ByteOffset)],
        };
    }
}
