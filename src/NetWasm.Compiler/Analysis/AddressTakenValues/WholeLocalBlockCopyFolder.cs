using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.AddressTakenValues;

internal interface IWholeLocalBlockCopyFolder
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// A block copy of exactly a value-type local's size from one local of that type
// to another, as Unsafe.CopyBlock(&destination, &source, sizeof(T)) inlines,
// copies the local, as RyuJIT morphs it into a struct assignment: cpblk becomes
// ldloc and stloc, and the addresses that fed it go, through the conversions
// and the inliner's temporaries that carried them, so neither local stays
// addressable for it. The size is sizeof of the locals' type, or a constant
// equal to the size of a primitive, a vector or a struct without instance
// fields that declares its size. Runs after idempotent loop collapse, which
// recognizes the block copy a loop repeats. A destination the copy alone
// stores, read only later in the copy's block, reads the source instead, so
// the copy goes, as Roslyn keeps such a temporary on the stack: a struct local
// lives in frame memory, where no Wasm pass removes a dead copy.
internal sealed class WholeLocalBlockCopyFolder(
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    ICalledMethodResolver calledMethods,
    ITypeDefinitionResolver typeDefinitions,
    IFieldRepository fields,
    bool enabled) : IWholeLocalBlockCopyFolder
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled || !body.Instructions.Any(static instruction => instruction.Operation == CilOperation.CopyBlock))
        {
            return body;
        }

        var instructions = body.Instructions;
        var entryStacks = _stacks.Validate(_graphs.Build(body)).InstructionEntryStacks;
        var producers = new StackSlotProducers(body, entryStacks);
        var accesses = Accesses(instructions);
        List<CilInstruction>[]? replacements = null;
        var removed = new HashSet<int>();
        var folded = new HashSet<int>();
        for (var index = 0; index < instructions.Length; index++)
        {
            if (instructions[index].Operation != CilOperation.CopyBlock ||
                index > 0 && instructions[index - 1].Operation == CilOperation.Volatile ||
                !entryStacks.TryGetValue(instructions[index].Offset, out var stack) || stack.Length < 3)
            {
                continue;
            }
            // An unaligned. prefix has the copy's stack; its operands are found from it.
            var consumer = index > 0 && instructions[index - 1].Operation == CilOperation.Unaligned ? index - 1 : index;
            var chain = new List<int>();
            if (Address(consumer, stack.Length - 3, producers, instructions, accesses, chain) is not { } destination ||
                Address(consumer, stack.Length - 2, producers, instructions, accesses, chain) is not { } source ||
                Length(consumer, stack.Length - 1, producers, instructions, accesses, chain) is not { } length ||
                destination == source ||
                (uint)destination >= (uint)body.LocalSignatureTypes.Length ||
                body.LocalSignatureTypes[destination] is not { IsValueType: true } type ||
                !type.Equals(body.LocalSignatureTypes[source]) ||
                !Covers(length, type) ||
                chain.Any(removed.Contains))
            {
                continue;
            }
            replacements ??= [.. instructions.Select(static instruction => new List<CilInstruction> { instruction })];
            // A branch to a removed instruction lands on the one after it.
            foreach (var at in chain)
            {
                removed.Add(at);
                replacements[at] = [];
            }
            if (index > 0 && instructions[index - 1].Operation == CilOperation.Unaligned)
            {
                replacements[index - 1] = [];
            }
            replacements[index] =
            [
                instructions[index] with { Operation = CilOperation.LoadLocal, Operand = new CilOperand.Index(source) },
                new CilInstruction(0, 1, CilOperation.StoreLocal, new CilOperand.Index(destination)),
            ];
            folded.Add(destination);
        }
        return replacements is null ? body : Propagate(RenumberedInstructions.Splice(body, replacements), folded);
    }

    // Each folded copy, ldloc source then stloc destination, whose destination
    // nothing else stores or addresses and the copy's block alone reads, after
    // it, of a source nothing stores or addresses after the copy's own read:
    // those reads take the source, and the copy goes.
    private static CilMethodBody Propagate(CilMethodBody body, HashSet<int> folded)
    {
        var instructions = body.Instructions;
        var accesses = Accesses(instructions);
        var entries = InstructionEntries.Of(body);
        List<CilInstruction>[]? replacements = null;
        for (var store = 1; store < instructions.Length; store++)
        {
            if (instructions[store] is not { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var destination } } ||
                !folded.Contains(destination) ||
                instructions[store - 1] is not { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var source } } ||
                source == destination || entries.Contains(instructions[store].Offset) ||
                accesses.Stores[destination] is not [_] || accesses.Addressed.Contains(destination) ||
                accesses.Addressed.Contains(source) ||
                (uint)destination >= (uint)body.LocalSignatureTypes.Length ||
                body.LocalSignatureTypes[destination] is not { IsValueType: true } type ||
                !type.Equals(body.LocalSignatureTypes[source]))
            {
                continue;
            }
            var reads = new List<int>();
            var sourceStored = false;
            var leaves = false;
            for (var index = store + 1; index < instructions.Length && reads.Count < accesses.Loads.GetValueOrDefault(destination); index++)
            {
                if (entries.Contains(instructions[index].Offset))
                {
                    leaves = true;
                    break;
                }
                switch (instructions[index])
                {
                    case { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var read } } when read == destination:
                        if (sourceStored) leaves = true;
                        reads.Add(index);
                        break;
                    case { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var written } } when written == source:
                        sourceStored = true;
                        break;
                }
                if (leaves) break;
            }
            if (leaves || reads.Count != accesses.Loads.GetValueOrDefault(destination)) continue;
            replacements ??= [.. instructions.Select(static instruction => new List<CilInstruction> { instruction })];
            replacements[store - 1] = [];
            replacements[store] = [];
            foreach (var read in reads)
            {
                replacements[read] = [instructions[read] with { Operand = new CilOperand.Index(source) }];
            }
        }
        return replacements is null ? body : RenumberedInstructions.Splice(body, replacements);
    }

    // The local whose address the operand carries: an ldloca behind native-int
    // conversions, Unsafe's views of the same address and temporaries, each
    // instruction of which joins the chain.
    private int? Address(int consumer, int slot, StackSlotProducers producers,
        IReadOnlyList<CilInstruction> instructions, SlotAccesses accesses, List<int> chain)
    {
        if (producers.Find(consumer, slot) is not { } index) return null;
        while (true)
        {
            switch (instructions[index])
            {
                case { Operation: CilOperation.ConvertNativeUInt or CilOperation.ConvertNativeInt }:
                    chain.Add(index);
                    if (producers.FindTop(index) is not { } converted) return null;
                    index = converted;
                    continue;
                case { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var local } }:
                    chain.Add(index);
                    return local;
                case { Operation: CilOperation.Call } when IsView(instructions[index]):
                    chain.Add(index);
                    if (producers.FindTop(index) is not { } viewed) return null;
                    index = viewed;
                    continue;
                case { Operation: CilOperation.LoadLocal } when Temporary(index, producers, instructions, accesses, chain) is { } stored:
                    index = stored;
                    continue;
                default:
                    return null;
            }
        }
    }

    // Unsafe.As<TFrom, TTo>(ref TFrom), AsRef<T>(ref readonly T) and
    // AsPointer<T>(ref readonly T): the address they are given, as another type.
    private bool IsView(CilInstruction instruction) => _calledMethods.Resolve(instruction) is
    {
        DeclaringType.FullName: "System.Runtime.CompilerServices.Unsafe",
        Definition: { IsStatic: true, Name: "As" or "AsRef" or "AsPointer" } definition,
        Signature.ParameterTypes: [CliValueKind.ManagedAddress],
    } && (definition.Name != "As" || definition.GenericArity == 2);

    // The copy's length: a constant or a size, behind conversions and temporaries.
    private ByteCount? Length(int consumer, int slot, StackSlotProducers producers,
        IReadOnlyList<CilInstruction> instructions, SlotAccesses accesses, List<int> chain)
    {
        if (producers.Find(consumer, slot) is not { } index) return null;
        while (true)
        {
            switch (instructions[index])
            {
                case { Operation: CilOperation.ConvertNativeUInt or CilOperation.ConvertNativeInt }:
                case { Operation: CilOperation.ConvertNumeric, Operand: CilOperand.NumericConversion { Checked: false } }:
                    chain.Add(index);
                    if (producers.FindTop(index) is not { } converted) return null;
                    index = converted;
                    continue;
                case { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: var value } }:
                    chain.Add(index);
                    return new ByteCount(value, null);
                case { Operation: CilOperation.SizeOf, Operand: CilOperand.TypeIdentity { Value: var sized } }:
                    chain.Add(index);
                    return new ByteCount(null, sized);
                case { Operation: CilOperation.Call } when _calledMethods.Resolve(instructions[index]) is
                {
                    Definition: { Name: "SizeOf", IsStatic: true },
                    DeclaringType.FullName: "System.Runtime.CompilerServices.Unsafe",
                    MethodArguments: [var argument],
                }:
                    chain.Add(index);
                    return new ByteCount(null, argument);
                case { Operation: CilOperation.LoadLocal } when Temporary(index, producers, instructions, accesses, chain) is { } stored:
                    index = stored;
                    continue;
                default:
                    return null;
            }
        }
    }

    // A temporary's load, when the temporary is stored once and read once, in
    // the same block before its load, and its address is never taken: the
    // producer of the stored value, with the load and the store joining the chain.
    private static int? Temporary(int load, StackSlotProducers producers, IReadOnlyList<CilInstruction> instructions,
        SlotAccesses accesses, List<int> chain)
    {
        var temporary = ((CilOperand.Index)instructions[load].Operand).Value;
        if (!accesses.Stores.TryGetValue(temporary, out var stores) || stores is not [var store] ||
            accesses.Loads.GetValueOrDefault(temporary) != 1 || accesses.Addressed.Contains(temporary) || store > load)
        {
            return null;
        }
        for (var at = store + 1; at <= load; at++)
        {
            if (producers.Enters(at)) return null;
        }
        if (producers.FindTop(store) is not { } value) return null;
        chain.Add(load);
        chain.Add(store);
        return value;
    }

    // Whether the length is the whole of a value of the type.
    private bool Covers(ByteCount length, CliTypeIdentity type)
    {
        if (length.Type is { } sized) return sized.Equals(type);
        if (KnownSizeRewriter.Size(type) is { } known) return known == length.Value;
        if (type.Shape != CliTypeShape.Named || type.ContainsGenericParameters) return false;
        var definition = _typeDefinitions.ResolveTypeIdentity(type);
        return definition.DeclaredSize != 0 && definition.DeclaredSize == length.Value &&
            definition.Fields.All(key => _fields.GetField(key) is { IsStatic: true } or { IsLiteral: true });
    }

    private static SlotAccesses Accesses(IReadOnlyList<CilInstruction> instructions)
    {
        var stores = new Dictionary<int, List<int>>();
        var loads = new Dictionary<int, int>();
        var addressed = new HashSet<int>();
        for (var index = 0; index < instructions.Count; index++)
        {
            switch (instructions[index])
            {
                case { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var local } }:
                    if (!stores.TryGetValue(local, out var list)) stores[local] = list = [];
                    list.Add(index);
                    break;
                case { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } }:
                    loads[local] = loads.GetValueOrDefault(local) + 1;
                    break;
                case { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var local } }:
                    addressed.Add(local);
                    break;
            }
        }
        return new(stores, loads, addressed);
    }

    private sealed record SlotAccesses(Dictionary<int, List<int>> Stores, Dictionary<int, int> Loads, HashSet<int> Addressed);

    // A constant byte count, or the size of a type.
    private sealed record ByteCount(int? Value, CliTypeIdentity? Type);
}
