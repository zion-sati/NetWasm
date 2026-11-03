using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using Slot = (bool IsArgument, int Index);

namespace NetWasm.Compiler.Analysis;

internal interface IFieldLoadReuser
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// A field read again while it still holds the value an earlier read found
// takes that value from a local instead: `this._m[this._j] = k` after
// `this._m[this._i] - this._m[this._j]` reads _m and _j once. The read is a
// local or argument's field (`ldloc s; ldfld f`) or a static field, of a value
// that is no struct. It is available where every path to it reads it after
// the last thing that could change it: a store to the field of any object, a
// store to the slot, a call other than a pure Math one, a store through an
// address, or, for statics, a first access to another type's statics, whose
// initializer may run. Slots whose address the method takes and fields whose
// address it takes are left alone, as are methods with exception handlers.
// Every read of a field that a later read reuses keeps its value in the
// field's local, and the reuse loads the local.
internal sealed class FieldLoadReuser(
    IControlFlowGraphBuilder graphs,
    ICalledMethodResolver calledMethods,
    IFieldRepository fields,
    bool enabled) : IFieldLoadReuser
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));

    // A slot's field, or a static field when Owner is null.
    private readonly record struct Key(Slot? Owner, string Field, EntityKey Definition, string DeclaringType);

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled || !body.ExceptionRegions.IsEmpty) return body;
        var instructions = body.Instructions;
        var exposed = instructions
            .Where(static instruction => instruction.Operation is CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress)
            .Select(static instruction => new Slot(instruction.Operation == CilOperation.LoadArgumentAddress,
                ((CilOperand.Index)instruction.Operand).Value))
            .ToHashSet();
        var addressed = instructions
            .Where(static instruction => instruction.Operation is CilOperation.LoadFieldAddress or CilOperation.LoadStaticFieldAddress)
            .Select(instruction => CilOperands.FieldKey(instruction.Operand))
            .ToHashSet();

        var graph = _graphs.Build(body);
        var starts = graph.Blocks.Select(static block => block.StartOffset).ToHashSet();
        // The reads that can take part: the instruction index of the ldfld or ldsfld, with its key and type.
        var reads = new Dictionary<int, (Key Key, CliTypeIdentity Type)>();
        for (var index = 0; index < instructions.Length; index++)
        {
            if (Read(instructions, index, starts) is { } read && !addressed.Contains(read.Key.Definition) &&
                !(read.Key.Owner is { } owner && exposed.Contains(owner)) &&
                read.Type.StackKind is not (CliValueKind.ValueType or CliValueKind.Unknown))
            {
                reads[index] = read;
            }
        }
        if (reads.Count < 2 || reads.Values.Select(static read => read.Key).Distinct().Count() == reads.Count) return body;

        var indices = Enumerable.Range(0, instructions.Length).ToDictionary(index => instructions[index].Offset);
        var keys = reads.Values.Select(static read => read.Key).ToHashSet();
        // Available reads at each block's entry: the meet of its predecessors' exits, from all for every block but the entry.
        var entries = new Dictionary<int, HashSet<Key>>();
        var exits = new Dictionary<int, HashSet<Key>>();
        var order = graph.Blocks.Where(block => graph.ReachableBlocks.Contains(block.Index)).Select(static block => block.Index).ToArray();
        foreach (var block in order) exits[block] = [.. keys];
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var block in order)
            {
                var entry = block == graph.Entry.Index
                    ? []
                    : Meet(graph.Predecessors[block].Where(exits.ContainsKey).Select(predecessor => exits[predecessor]));
                entries[block] = entry;
                var exit = new HashSet<Key>(entry);
                foreach (var instruction in graph.GetBlock(block).Instructions)
                {
                    Transfer(exit, indices[instruction.Offset], instructions, reads);
                }
                if (!exit.SetEquals(exits[block]))
                {
                    exits[block] = exit;
                    changed = true;
                }
            }
        }

        var reused = new HashSet<int>();
        foreach (var block in order)
        {
            var available = new HashSet<Key>(entries[block]);
            foreach (var instruction in graph.GetBlock(block).Instructions)
            {
                var index = indices[instruction.Offset];
                if (reads.TryGetValue(index, out var read) && available.Contains(read.Key)) reused.Add(index);
                Transfer(available, index, instructions, reads);
            }
        }
        if (reused.Count == 0) return body;

        var locals = body.Locals.ToBuilder();
        var localTypes = body.LocalSignatureTypes.ToBuilder();
        var cached = new Dictionary<Key, int>();
        foreach (var key in reused.Select(index => reads[index].Key).Distinct())
        {
            var type = reads.Values.First(read => read.Key == key).Type;
            locals.Add(type.StackKind);
            localTypes.Add(type);
            cached[key] = locals.Count - 1;
        }
        var replacements = instructions.Select(static instruction => new List<CilInstruction> { instruction }).ToArray();
        foreach (var (index, (key, _)) in reads)
        {
            if (!cached.TryGetValue(key, out var local)) continue;
            var load = new CilInstruction(0, 1, CilOperation.LoadLocal, new CilOperand.Index(local));
            if (reused.Contains(index))
            {
                // The slot load goes with the field read; its offset keeps the local load.
                if (key.Owner is not null)
                {
                    replacements[index - 1] = [load];
                    replacements[index] = [];
                }
                else
                {
                    replacements[index] = [load];
                }
                continue;
            }
            replacements[index] = [instructions[index], new(0, 1, CilOperation.StoreLocal, new CilOperand.Index(local)), load];
        }
        return RenumberedInstructions.Splice(body, replacements) with
        {
            Locals = locals.ToImmutable(),
            LocalSignatureTypes = localTypes.ToImmutable(),
        };
    }

    // What every predecessor's exit holds. Without protected regions, each
    // reachable block but the entry has a reachable predecessor.
    private static HashSet<Key> Meet(IEnumerable<HashSet<Key>> predecessors) =>
        predecessors.Aggregate(static (meet, predecessor) => [.. meet.Intersect(predecessor)]);

    // A slot's field read, `ldloc s; ldfld f` within one block, or a static field read.
    private (Key Key, CliTypeIdentity Type)? Read(ImmutableArray<CilInstruction> instructions, int index, HashSet<int> starts)
    {
        var instruction = instructions[index];
        if (instruction.Operation is not (CilOperation.LoadField or CilOperation.LoadStaticField) ||
            FieldOperand.Describe(instruction.Operand, _fields) is not { } field)
        {
            return null;
        }
        if (instruction.Operation == CilOperation.LoadStaticField)
        {
            return index > 0 && instructions[index - 1].Operation == CilOperation.Volatile
                ? null
                : (new Key(null, field.Field, field.Definition, field.DeclaringType), field.Type);
        }
        if (index == 0 || starts.Contains(instruction.Offset)) return null;
        return Owner(instructions[index - 1]) is { } owner
            ? (new Key(owner, field.Field, field.Definition, field.DeclaringType), field.Type)
            : null;
    }

    private static Slot? Owner(CilInstruction instruction) => instruction switch
    {
        { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } } => new Slot(false, local),
        { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: var argument } } => new Slot(true, argument),
        _ => null,
    };

    // What the instruction leaves available: it kills what it may change, then a read adds its own.
    private void Transfer(HashSet<Key> available, int index, ImmutableArray<CilInstruction> instructions,
        Dictionary<int, (Key Key, CliTypeIdentity Type)> reads)
    {
        var instruction = instructions[index];
        switch (instruction.Operation)
        {
            case CilOperation.StoreField:
                var written = CilOperands.FieldKey(instruction.Operand);
                available.RemoveWhere(key => key.Owner is not null && key.Definition == written);
                break;
            case CilOperation.StoreLocal or CilOperation.StoreArgument:
                var stored = new Slot(instruction.Operation == CilOperation.StoreArgument, ((CilOperand.Index)instruction.Operand).Value);
                available.RemoveWhere(key => EqualityComparer<Slot?>.Default.Equals(key.Owner, stored));
                break;
            case CilOperation.LoadStaticField or CilOperation.StoreStaticField or CilOperation.LoadStaticFieldAddress:
                // A type's first static access may run its initializer; once one of
                // its statics is known read, it has run.
                var accessed = FieldOperand.Describe(instruction.Operand, _fields);
                if (accessed is not { } statics ||
                    !available.Any(key => key.Owner is null && key.DeclaringType == statics.DeclaringType))
                {
                    available.Clear();
                }
                if (instruction.Operation == CilOperation.StoreStaticField)
                {
                    var assigned = CilOperands.FieldKey(instruction.Operand);
                    available.RemoveWhere(key => key.Owner is null && key.Definition == assigned);
                }
                break;
            case CilOperation.Call or CilOperation.CallVirtual or CilOperation.NewObject:
                if (!(instruction.Operation == CilOperation.Call && _calledMethods.Resolve(instruction) is { } target &&
                        LoopInvariantCallHoister.IsPure(target)))
                {
                    available.Clear();
                }
                break;
            case CilOperation.CallIndirect or CilOperation.StoreObject or CilOperation.CopyObject or CilOperation.CopyBlock or
                CilOperation.InitializeBlock or CilOperation.InitializeObject or CilOperation.CompareExchange:
                available.Clear();
                break;
        }
        if (reads.TryGetValue(index, out var read)) available.Add(read.Key);
    }
}
