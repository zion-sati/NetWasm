using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

// One method's aggregate value analysis: its candidate storages, the ones rejected so far, and what the collaborators
// record for the rest (lane accesses, root dependencies, materializations and whole-value stores).
internal sealed class ManagedAggregateAnalysisSession(
    ManagedAggregateValueAnalysisRequest request,
    Dictionary<ManagedAggregateStorageId, ManagedAggregateStorage> storages)
{
    internal ManagedAggregateValueAnalysisRequest Request { get; } = request;

    internal Dictionary<ManagedAggregateStorageId, ManagedAggregateStorage> Storages { get; } = storages;

    internal HashSet<ManagedAggregateStorageId> Rejected { get; } = [];

    internal Dictionary<int, ManagedAggregateLaneAccess> Accesses { get; } = [];

    internal List<ManagedAggregateRootDependency> RootDependencies { get; } = [];

    internal List<ManagedAggregateMaterialization> Materializations { get; } = [];

    internal List<(StructuredBlockId Block, ManagedAggregateStorageId Storage)> WholeValueStores { get; } = [];

    internal void Reject(ManagedAggregateAddressOrigins origins)
    {
        foreach (var recipe in origins.Recipes)
        {
            Reject(recipe.Storage);
        }
    }

    internal void Reject(ManagedAggregateStorageId storage)
    {
        Rejected.Add(storage);
    }

    // The values an instruction consumes leave the analysis: whatever they may address is rejected.
    internal void RejectConsumed(IReadOnlyList<ManagedAggregateAddressOrigins> stack, int consumed)
    {
        for (var index = stack.Count - consumed; index < stack.Count; index++)
        {
            Reject(stack[index]);
        }
    }

    internal bool TrySingleRecipe(
        ManagedAggregateAddressOrigins origins,
        out ManagedAggregateAddressRecipe recipe)
    {
        if (!origins.Unknown && origins.Recipes.Count == 1)
        {
            recipe = origins.Recipes.Single();
            return true;
        }
        Reject(origins);
        recipe = default;
        return false;
    }

    // The address of a whole argument or local: its storage if it is a candidate, anything otherwise.
    internal ManagedAggregateAddressOrigins RootOrigins(ManagedAggregateStorageKind kind, int index)
    {
        var id = new ManagedAggregateStorageId(kind, index);
        return Storages.TryGetValue(id, out var storage)
            ? new(false, [new(id, 0, storage.Type)])
            : ManagedAggregateAddressOrigins.UnknownOnly;
    }

    internal void RecordWholeValueMaterialization(
        int instructionOffset,
        ManagedAggregateStorageKind kind,
        int index)
    {
        var id = new ManagedAggregateStorageId(kind, index);
        if (Storages.TryGetValue(id, out var storage))
        {
            Materializations.Add(new(instructionOffset, storage.Id));
        }
    }

    // A store of the whole value writes the backing storage, as a call given
    // its address may; the lanes reload from it afterwards.
    internal void RecordWholeValueStore(
        int instructionOffset,
        StructuredBlockId block,
        ManagedAggregateStorageKind kind,
        int index)
    {
        var id = new ManagedAggregateStorageId(kind, index);
        if (Storages.ContainsKey(id))
        {
            Materializations.Add(new(instructionOffset, id, ReloadAfter: true));
            WholeValueStores.Add((block, id));
        }
    }

    internal void RecordAccess(
        CilInstruction instruction,
        StructuredBlockId block,
        ManagedAggregateAddressRecipe address,
        int size,
        CliValueKind kind,
        bool writes)
    {
        var storage = Storages[address.Storage];
        if (address.ByteOffset < 0 ||
            address.ByteOffset + size > storage.Size)
        {
            Reject(address.Storage);
            return;
        }
        // An access that touches a reference or byref lane must be exactly that lane.
        var covered = storage.Lanes
            .Where(lane => lane.ByteOffset < address.ByteOffset + size && address.ByteOffset < lane.ByteOffset + lane.Size)
            .ToArray();
        var tracked = covered.Any(static lane => lane.Kind != CliValueKind.I4);
        if (tracked && (covered.Length != 1 || covered[0] != new ManagedAggregateLaneShape(address.ByteOffset, size, kind)))
        {
            Reject(address.Storage);
            return;
        }
        Accesses[instruction.Offset] = new(
            instruction.Offset,
            block,
            address,
            size,
            kind,
            writes)
        {
            LaneOffsets = tracked ? [covered[0].ByteOffset] : [],
        };
    }
}
