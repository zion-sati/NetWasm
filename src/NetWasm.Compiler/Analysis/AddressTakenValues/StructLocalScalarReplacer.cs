// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.AddressTakenValues;

internal interface IStructLocalScalarReplacer
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// A struct local whose address is only taken to read, write or clear its
// fields, as inlined constructors, operators and accessors leave it, becomes a
// local per field, which needs no value-frame storage and stays in registers.
// Copies between two such locals copy field by field, also when other locals'
// loads and stores come between, as an inlined call's argument spill puts
// them; any other whole load or store goes through one temporary of the struct
// type. A
// store to a narrow field truncates, as a store to a local of that type does
// not, so it gains the conversion. A whole load whose dups a deconstruction's
// field reads take reads the field locals instead. A whole store straight from
// memory, as a foreach's element copy out of an array or a span is, loads only
// the fields the local's reads need from the element's address, and no
// temporary. A struct of declared size is split only when its whole value is
// never read again, which is the only way its padding could be seen. A struct
// argument whose address is taken, as an inlined call on a span parameter
// takes it, is split as a local the method copies it into on entry, where it
// splits. .NET's JIT promotes struct locals and arguments the same way.
internal sealed class StructLocalScalarReplacer(
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IControlFlowNaturalLoopAnalyzer loops,
    ITypeDefinitionResolver typeDefinitions,
    IFieldRepository fields,
    IVectorLaneFields lanes,
    IStructLocalJoinFinder joins,
    IStructJoinSplitConstraint joinSplits,
    IStackDepthFitter depths,
    bool enabled) : IStructLocalScalarReplacer
{
    // More fields than this are more locals than registers to keep them in.
    internal const int MaximumFields = 8;

    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly IControlFlowNaturalLoopAnalyzer _loops = loops ?? throw new ArgumentNullException(nameof(loops));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly IVectorLaneFields _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
    private readonly IStructLocalJoinFinder _joins = joins ?? throw new ArgumentNullException(nameof(joins));
    private readonly IStructJoinSplitConstraint _joinSplits = joinSplits ??
        throw new ArgumentNullException(nameof(joinSplits));
    private readonly IStackDepthFitter _depths = depths ?? throw new ArgumentNullException(nameof(depths));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled) return body;
        // The entry copy costs a copy of each argument that stays whole, so
        // only those that split keep it.
        var arguments = AddressedStructArguments(body);
        if (arguments.Length != 0)
        {
            var promoted = Split(WithArgumentLocals(body, arguments), out var split);
            var kept = arguments.Where((_, index) => split.Contains(body.Locals.Length + index)).ToImmutableArray();
            if (kept.Length == arguments.Length) return promoted;
            if (kept.Length != 0) return Split(WithArgumentLocals(body, kept), out _);
        }
        return Split(body, out _);
    }

    // The arguments past this whose address is taken and whose type splits.
    private ImmutableArray<(int Argument, CliTypeIdentity Type)> AddressedStructArguments(CilMethodBody body)
    {
        var parameters = (body.MethodInstance?.Signature ?? body.Method.Signature).ParameterSignatureTypes;
        var first = body.Method.IsStatic ? 0 : 1;
        return
        [
            .. body.Instructions
                // Every struct argument, not only addressed ones: a split one is read from its fields,
                // which the convention passes as parameters.
                .Select(instruction => instruction is
                {
                    Operation: CilOperation.LoadArgumentAddress or CilOperation.LoadArgument or CilOperation.StoreArgument,
                    Operand: CilOperand.Index { Value: var argument },
                } && argument >= first && argument - first < parameters.Length ? argument : -1)
                .Where(argument => argument >= 0)
                .Distinct()
                .Order()
                .Select(argument => (argument, parameters[argument - first]))
                .Where(argument => Fields(argument.Item2, out _) is not null),
        ];
    }

    // The body with each of the arguments in a new local, after the locals it
    // has, that it is copied into on entry and read, written and addressed in
    // place of the argument. The copy reads the argument through its address,
    // as a whole store from memory, so the split loads each field from it.
    private static CilMethodBody WithArgumentLocals(
        CilMethodBody body,
        ImmutableArray<(int Argument, CliTypeIdentity Type)> arguments)
    {
        var locals = arguments
            .Select((argument, index) => KeyValuePair.Create(argument.Argument, body.Locals.Length + index))
            .ToDictionary();
        var replacements = body.Instructions
            .Select(instruction => new List<CilInstruction>
            {
                instruction is { Operand: CilOperand.Index { Value: var argument } } &&
                    locals.TryGetValue(argument, out var local) &&
                    instruction.Operation switch
                    {
                        CilOperation.LoadArgument => CilOperation.LoadLocal,
                        CilOperation.LoadArgumentAddress => CilOperation.LoadLocalAddress,
                        CilOperation.StoreArgument => CilOperation.StoreLocal,
                        _ => (CilOperation?)null,
                    } is { } operation
                    ? instruction with { Operation = operation, Operand = new CilOperand.Index(local) }
                    : instruction,
            })
            .ToArray();
        var entry = arguments.SelectMany(argument => new[]
        {
            Synthetic(CilOperation.LoadArgumentAddress, new CilOperand.Index(argument.Argument)),
            Synthetic(CilOperation.LoadObject, new CilOperand.TypeIdentity(argument.Type)),
            Local(CilOperation.StoreLocal, locals[argument.Argument]),
        });
        return RenumberedInstructions.Splice(body, replacements, [.. entry]) with
        {
            Locals = body.Locals.AddRange(arguments.Select(argument => argument.Type.StackKind)),
            LocalSignatureTypes = body.LocalSignatureTypes.AddRange(arguments.Select(argument => argument.Type)),
        };
    }

    private CilMethodBody Split(CilMethodBody body, out HashSet<int> split)
    {
        split = [];
        var layouts = new Dictionary<int, ImmutableArray<Field>>();
        var sized = new HashSet<int>();
        foreach (var instruction in body.Instructions)
        {
            if (instruction is { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var local } } &&
                (uint)local < (uint)body.LocalSignatureTypes.Length && !layouts.ContainsKey(local) &&
                Fields(body.LocalSignatureTypes[local], out var declaredSize) is { } localFields)
            {
                layouts.Add(local, localFields);
                if (declaredSize) sized.Add(local);
            }
        }
        // A struct local only stored and loaded whole, as a method's return local is (Roslyn stores each returned value
        // into it before a leave and loads it for the one ret), splits when a candidate's value is copied into it.
        var copyOnly = new HashSet<int>();
        foreach (var instruction in body.Instructions)
        {
            if (instruction is { Operation: CilOperation.StoreLocal or CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } } &&
                (uint)local < (uint)body.LocalSignatureTypes.Length && !layouts.ContainsKey(local) &&
                Fields(body.LocalSignatureTypes[local], out var declaredSize) is { } localFields && !declaredSize)
            {
                layouts.Add(local, localFields);
                copyOnly.Add(local);
            }
        }
        if (layouts.Count == 0) return body;

        var graph = _graphs.Build(body);
        var entryStacks = _stacks.Validate(graph).InstructionEntryStacks;
        var producers = new StackSlotProducers(body, entryStacks);
        var instructions = body.Instructions;
        var plans = new Dictionary<int, Plan>();
        // The instructions that push a candidate's address, ldloca and the dup
        // of one, and how many of the addresses they push a planned consumer takes.
        var addresses = new Dictionary<int, int>();
        // The instructions that push a field's address, its ldflda and the dup
        // of one, counted the same way.
        var fieldAddresses = new Dictionary<int, FieldAddress>();
        var consumed = new Dictionary<int, int>();
        var accessed = new HashSet<int>();
        // The dups of a candidate's whole value, as a deconstruction chains them,
        // with the load they copy, and the instructions that take their copies.
        var wholeDuplicates = new Dictionary<int, (int Load, int Local)>();
        var duplicateConsumers = new HashSet<(int Consumer, int Duplicate)>();
        for (var index = 0; index < instructions.Length; index++)
        {
            var instruction = instructions[index];
            if (!entryStacks.TryGetValue(instruction.Offset, out var stack))
            {
                continue;
            }
            if (instruction is { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var addressed } } &&
                layouts.ContainsKey(addressed))
            {
                addresses[index] = addressed;
                continue;
            }
            // A compound assignment to a field duplicates its address.
            if (instruction.Operation == CilOperation.Duplicate &&
                producers.FindTop(index) is { } duplicated && addresses.TryGetValue(duplicated, out var duplicatedLocal))
            {
                addresses[index] = duplicatedLocal;
                plans[duplicated] = new Removed(duplicatedLocal);
                plans[index] = new Removed(duplicatedLocal);
                consumed[duplicated] = consumed.GetValueOrDefault(duplicated) + 1;
                continue;
            }
            if (instruction.Operation == CilOperation.Duplicate &&
                producers.FindTop(index) is { } duplicatedField &&
                fieldAddresses.TryGetValue(duplicatedField, out var duplicatedAddress))
            {
                fieldAddresses[index] = duplicatedAddress;
                consumed[duplicatedField] = consumed.GetValueOrDefault(duplicatedField) + 1;
                continue;
            }
            var slot = instruction.Operation switch
            {
                CilOperation.LoadField or CilOperation.LoadFieldAddress or CilOperation.LoadObject or
                    CilOperation.InitializeObject or CilOperation.Pop => stack.Length - 1,
                CilOperation.StoreField or CilOperation.StoreObject => stack.Length - 2,
                _ => -1,
            };
            // A prefixed access keeps its memory semantics.
            if (slot < 0 || index > 0 && instructions[index - 1].Operation is CilOperation.Volatile or CilOperation.Unaligned ||
                producers.Find(index, slot, duplicates: true) is not { } producer)
            {
                continue;
            }
            // A field's address may only read, write or clear that field. Any
            // other use, such as Unsafe.Add or a callee, can reach the struct's
            // other fields, which a split no longer keeps beside it.
            if (fieldAddresses.TryGetValue(producer, out var fieldAddress))
            {
                if (AccessesField(instruction, fieldAddress.Field.Type))
                {
                    consumed[producer] = consumed.GetValueOrDefault(producer) + 1;
                }
                else if (VectorLane(instruction, fieldAddress.Field.Type) is { } lane)
                {
                    // A vector field's lane read or written by value stays within the field, and so does a lane's
                    // address that only its own reads and writes take.
                    consumed[producer] = consumed.GetValueOrDefault(producer) + 1;
                    if (instruction.Operation == CilOperation.LoadFieldAddress)
                    {
                        fieldAddresses[index] = new FieldAddress(fieldAddress.Local, lane);
                    }
                }
                continue;
            }
            int local;
            bool address;
            if (instruction.Operation == CilOperation.LoadField &&
                instructions[producer].Operation == CilOperation.Duplicate &&
                WholeLoad(index, producer) is { } whole &&
                layouts.TryGetValue(whole.Local, out var wholeFields) &&
                FieldOf(wholeFields, instruction.Operand) is { } wholeField)
            {
                plans[index] = new LoadField(whole.Local, wholeField);
                accessed.Add(whole.Local);
                continue;
            }
            if (addresses.TryGetValue(producer, out local))
            {
                address = true;
            }
            else if (instructions[producer] is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var loaded } } &&
                layouts.ContainsKey(loaded))
            {
                local = loaded;
                address = false;
            }
            else
            {
                continue;
            }
            var localFields = layouts[local];
            var type = body.LocalSignatureTypes[local];
            // A value ldfld reads or pop drops; every other access takes the
            // address. A field of another type, as Unsafe.As reads one, or a
            // whole access of another type keeps the struct whole.
            Plan? plan = instruction.Operation switch
            {
                CilOperation.LoadField =>
                    FieldOf(localFields, instruction.Operand) is { } field ? new LoadField(local, field) : null,
                _ when !address => null,
                CilOperation.LoadFieldAddress =>
                    FieldOf(localFields, instruction.Operand) is { } field ? new FieldAddress(local, field) : null,
                CilOperation.StoreField =>
                    FieldOf(localFields, instruction.Operand) is { } field &&
                        StoreConversion(field.Type, stack[^1]) is { } conversion
                        ? new StoreField(local, field, conversion)
                        : null,
                CilOperation.Pop => new Removed(local),
                _ when !Operates(instruction, type) => null,
                CilOperation.InitializeObject => new Clear(local),
                CilOperation.LoadObject => new LoadWhole(local),
                // Stobj of a value read whole from memory just before (a static field's, as
                // ArraySegment<T>.Empty is), which loads each field from the source's address.
                CilOperation.StoreObject when index > 0 && !plans.ContainsKey(index - 1) && !producers.Enters(index) &&
                    producers.FindTop(index) == index - 1 &&
                    entryStacks.TryGetValue(instructions[index - 1].Offset, out var storedStack) &&
                    IsMemorySource(instructions, index - 1, type, storedStack) => new StoreFromSource(local),
                // stobj, the last access the slot above takes.
                _ => new StoreWhole(local),
            };
            if (plan is null) continue;
            if (plan is StoreFromSource) plans[index - 1] = new SourceAddress(local);
            plans[producer] = new Removed(local);
            plans[index] = plan;
            if (address) consumed[producer] = consumed.GetValueOrDefault(producer) + 1;
            if (plan is FieldAddress pushed) fieldAddresses[index] = pushed;
            // A clear stores each field's zero, as a split local does.
            if (plan is LoadField or StoreField or FieldAddress or Clear) accessed.Add(local);
        }

        // A whole value read field by field through dups is gone once each dup's
        // two copies go to field reads or the next dup; one with another
        // consumer keeps the value whole.
        foreach (var (duplicate, (load, local)) in wholeDuplicates)
        {
            plans[duplicate] = new Removed(local);
            plans[load] = new Removed(local);
        }
        // Every address a candidate's ldloca or dup pushes must reach a planned
        // consumer, and every address of one of its fields an access to that
        // field, or the address escapes.
        var rejected = addresses
            .Concat(fieldAddresses.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Local)))
            .Where(pair => consumed.GetValueOrDefault(pair.Key) !=
                (instructions[pair.Key].Operation == CilOperation.Duplicate ? 2 : 1))
            .Select(pair => pair.Value)
            .Concat(wholeDuplicates
                .Where(pair => duplicateConsumers.Count(edge => edge.Duplicate == pair.Key) != 2)
                .Select(pair => pair.Value.Local))
            .Concat(AddressesLeavingBlocks(graph, instructions, entryStacks, producers, addresses, fieldAddresses))
            .ToHashSet();
        // Whole loads and stores, and the copies between two candidates of one type.
        for (var index = 0; index < instructions.Length; index++)
        {
            if (plans.ContainsKey(index) ||
                instructions[index] is not { Operand: CilOperand.Index { Value: var local } } instruction ||
                !layouts.ContainsKey(local) ||
                !entryStacks.ContainsKey(instruction.Offset))
            {
                continue;
            }
            switch (instruction.Operation)
            {
                case CilOperation.LoadLocal:
                    // A whole value stored straight into a field or an address of its own type, as a suspension saves
                    // an awaited value task into its frame, stores each of its fields there.
                    if (index + 1 < instructions.Length && !plans.ContainsKey(index + 1) &&
                        entryStacks.ContainsKey(instructions[index + 1].Offset) && !producers.Enters(index + 1) &&
                        producers.FindTop(index + 1) == index &&
                        WholeDestination(instructions[index + 1], body.LocalSignatureTypes[local]) is { } destination)
                    {
                        plans[index] = new LoadInto(local);
                        plans[index + 1] = new StoreInto(local, destination.Field);
                    }
                    else
                    {
                        plans[index] = new LoadWhole(local);
                    }
                    break;
                case CilOperation.StoreLocal:
                    // A copy from another candidate, also when other locals' loads
                    // and stores come between, as an inlined call's argument spill
                    // stores its arguments after loading them all.
                    if (producers.FindTop(index) is { } copied &&
                        plans.TryGetValue(copied, out var previous) &&
                        previous is LoadWhole { Local: var from } && from != local &&
                        body.LocalSignatureTypes[from].Equals(body.LocalSignatureTypes[local]) &&
                        !producers.Enters(index) &&
                        instructions[(copied + 1)..index].All(between => LeavesLocal(between, from)))
                    {
                        plans[copied] = new CopySource(from, local);
                        plans[index] = new CopyTarget(from, local);
                    }
                    // A join of whole values, as an inlined conditional construction leaves one (AsSpan's empty span
                    // or the span it builds): each path copies its candidate into this one where it loaded it, and
                    // the join stores nothing.
                    else if (producers.Enters(index) && _joins.Find(instructions, index, producer =>
                        entryStacks.ContainsKey(instructions[producer].Offset) &&
                        plans.TryGetValue(producer, out var loaded) && loaded is LoadWhole { Local: var from } &&
                        from != local && body.LocalSignatureTypes[from].Equals(body.LocalSignatureTypes[local])) is { } sources)
                    {
                        foreach (var source in sources)
                        {
                            plans[source] = new JoinCopy(plans[source].Local, local);
                        }
                        plans[index] = new JoinStore(local);
                    }
                    else if (index > 0 && !plans.ContainsKey(index - 1) && !producers.Enters(index) &&
                        producers.FindTop(index) == index - 1 &&
                        entryStacks.TryGetValue(instructions[index - 1].Offset, out var sourceStack) &&
                        IsMemorySource(instructions, index - 1, body.LocalSignatureTypes[local], sourceStack))
                    {
                        plans[index - 1] = new SourceAddress(local);
                        plans[index] = new StoreFromSource(local);
                    }
                    else
                    {
                        plans[index] = new StoreWhole(local);
                    }
                    break;
            }
        }
        // A whole store of a call's result, which a call returning the value in results leaves in lanes.
        var callResultStores = plans
            .Where(pair => pair.Value is StoreWhole && !producers.Enters(pair.Key) &&
                producers.FindTop(pair.Key) is { } producer &&
                instructions[producer].Operation is CilOperation.Call or CilOperation.CallVirtual)
            .Select(pair => pair.Key)
            .ToHashSet();
        // A struct of declared size keeps bytes beyond its fields, which only
        // a whole read of the value could expose.
        // Each copy's target sits with its source.
        rejected.UnionWith(sized.Where(local => plans.Values.Any(plan => plan switch
        {
            LoadWhole load => load.Local == local,
            LoadInto load => load.Local == local,
            CopySource copy => copy.From == local || copy.To == local,
            _ => false,
        })));
        // A candidate whose whole value only goes into a join is used through the join's target.
        var joinSources = plans.Values.OfType<JoinCopy>().ToLookup(static copy => copy.To, static copy => copy.From);
        split = layouts.Keys.Where(local => !rejected.Contains(local) && (accessed.Contains(local) ||
            plans.Values.OfType<JoinCopy>().Any(copy => copy.From == local && accessed.Contains(copy.To)) ||
            copyOnly.Contains(local) && plans.Values.Any(plan => plan is CopyTarget { To: var to } && to == local ||
                plan is JoinStore { Local: var joined } && joined == local) ||
            // A local cleared or built only to be copied into or out of an accessed one (`initobj V; ldloc V; stloc W`)
            // splits with it, or the copy would be a transfer keeping both whole.
            plans.Values.Any(plan => plan is CopySource { From: var from, To: var into } && from == local && accessed.Contains(into) ||
                plan is CopyTarget { From: var source, To: var target } && target == local && accessed.Contains(source)))).ToHashSet();
        _joinSplits.Apply(split, joinSources);
        if (split.Count != 0) KeepAmortizedTransfers(graph, instructions, plans, split, callResultStores, body.LocalSignatureTypes);
        if (split.Count == 0) return body;
        _joinSplits.Apply(split, joinSources);
        return split.Count == 0 ? body : _depths.Fit(Splice(body, layouts, split, plans, callResultStores));

        // The candidate's whole load a field read's dup copies, through the
        // chain of dups a deconstruction makes, recording who takes each copy.
        (int Load, int Local)? WholeLoad(int consumer, int duplicate)
        {
            var chain = new List<(int Consumer, int Duplicate)>();
            while (true)
            {
                chain.Add((consumer, duplicate));
                if (!entryStacks.TryGetValue(instructions[duplicate].Offset, out var duplicateStack) ||
                    producers.Find(duplicate, duplicateStack.Length - 1, duplicates: true) is not { } source)
                {
                    return null;
                }
                if (instructions[source].Operation == CilOperation.Duplicate)
                {
                    consumer = duplicate;
                    duplicate = source;
                    continue;
                }
                if (instructions[source] is not { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } } ||
                    !layouts.ContainsKey(local))
                {
                    return null;
                }
                foreach (var edge in chain)
                {
                    duplicateConsumers.Add(edge);
                    wholeDuplicates[edge.Duplicate] = (source, local);
                }
                return (source, local);
            }
        }
    }

    // Whether the whole load at the index is an argument of a call that follows it, with only pushes of
    // locals, arguments and constants between: a call taking the struct by its fields passes them from lanes.
    private bool ArgumentOfCall(ImmutableArray<CilInstruction> instructions, int index, CliTypeIdentity type)
    {
        // Only a struct the convention passes by its fields (at most four scalars), to a method with a body, with nothing
        // between that branches, calls or could collect: the call then takes it from lanes.
        if (Fields(type, out var declaredSize) is not { Length: <= 4 } || declaredSize) return false;
        var above = 0;
        for (var next = index + 1; next < instructions.Length; next++)
        {
            var instruction = instructions[next];
            if (instruction.Operation is CilOperation.Call or CilOperation.CallVirtual or CilOperation.NewObject)
            {
                if (instruction.Operand is not CilOperand.MethodInstance { Value: var called }) return false;
                var pops = called.Signature.ParameterTypes.Length +
                    (!called.Definition.IsStatic && instruction.Operation != CilOperation.NewObject ? 1 : 0);
                return pops > above && !called.Definition.IsIntrinsic && called.Definition.HasManagedBody;
            }
            if (!KeepsLanes(instruction.Operation) || CilStackEffects.Of(instruction) is not { } effect ||
                effect.Pops > above)
            {
                return false;
            }
            above += effect.Pushes - effect.Pops;
        }
        return false;
    }

    // Whether the instruction neither branches, calls, allocates, throws nor runs a class constructor.
    private static bool KeepsLanes(CilOperation operation) => operation is
        CilOperation.Nop or CilOperation.LoadArgument or CilOperation.LoadArgumentAddress or CilOperation.StoreArgument or
        CilOperation.LoadLocal or CilOperation.LoadLocalAddress or CilOperation.StoreLocal or CilOperation.LoadInt32 or
        CilOperation.LoadInt64 or CilOperation.LoadFloat32 or CilOperation.LoadFloat64 or CilOperation.LoadNull or
        CilOperation.Duplicate or CilOperation.Pop or CilOperation.LoadField or CilOperation.LoadFieldAddress or
        CilOperation.StoreField or CilOperation.InitializeObject or
        CilOperation.Add or CilOperation.Subtract or CilOperation.Multiply or CilOperation.BitwiseAnd or
        CilOperation.BitwiseOr or CilOperation.BitwiseXor or CilOperation.ShiftLeft or CilOperation.ShiftRightSigned or
        CilOperation.ShiftRightUnsigned or CilOperation.Negate or CilOperation.OnesComplement or
        CilOperation.ConvertInt32 or CilOperation.ConvertInt32Unsigned or CilOperation.ConvertInt64 or
        CilOperation.ConvertInt64Unsigned or CilOperation.ConvertNativeInt or CilOperation.ConvertNativeUInt or
        CilOperation.ConvertFloat32 or CilOperation.ConvertFloat64 or CilOperation.ConvertFloatUnsigned or
        CilOperation.ConvertNumeric or CilOperation.CompareEqual or CilOperation.CompareGreaterThanSigned or
        CilOperation.CompareGreaterThanUnsigned or CilOperation.CompareLessThanSigned or
        CilOperation.CompareLessThanUnsigned or CilOperation.LoadArrayLength or CilOperation.Select;

    // A whole load or store through the temporary copies the struct and every
    // field, so it pays off only where the fields are accessed in a loop it
    // sits outside of, as an enumerator assigned before the loop that moves it.
    // A struct copied out of an array on every iteration stays whole. Leaving
    // one local whole turns its copies with others into such transfers.
    private void KeepAmortizedTransfers(
        ControlFlowGraph graph,
        ImmutableArray<CilInstruction> instructions,
        Dictionary<int, Plan> plans,
        HashSet<int> split,
        HashSet<int> callResultStores,
        ImmutableArray<CliTypeIdentity> localTypes)
    {
        var blocks = graph.Blocks
            .SelectMany(block => block.Instructions.Select(instruction => (instruction.Offset, block.Index)))
            .ToDictionary(pair => pair.Offset, pair => pair.Index);
        var loops = _loops.Analyze(graph, graph.Entry.Index, graph.ReachableBlocks);
        // Blocks partition the body, so each instruction has one.
        int Depth(int index) => loops.Count(loop => loop.Contains(blocks[instructions[index].Offset]));
        bool changed;
        do
        {
            changed = false;
            foreach (var local in split.ToArray())
            {
                var deepestAccess = plans
                    .Where(pair => pair.Value is LoadField or StoreField or FieldAddress && pair.Value.Local == local)
                    .Select(pair => Depth(pair.Key))
                    .DefaultIfEmpty(-1)
                    .Max();
                if (plans.Any(pair => Transfers(pair.Key, pair.Value, local) && Depth(pair.Key) >= deepestAccess))
                {
                    split.Remove(local);
                    changed = true;
                }
            }
        }
        while (changed);

        bool Transfers(int index, Plan plan, int local) => plan switch
        {
            // The ret's whole load builds the value field by field where the convention returns it.
            LoadWhole when index + 1 < instructions.Length && instructions[index + 1].Operation == CilOperation.Return => false,
            LoadWhole when ArgumentOfCall(instructions, index, localTypes[plan.Local]) => false,
            StoreWhole when callResultStores.Contains(index) => false,
            LoadWhole or StoreWhole => plan.Local == local,
            CopySource copy => copy.From == local && !split.Contains(copy.To),
            CopyTarget copy => copy.To == local && !split.Contains(copy.From),
            JoinCopy copy => copy.From == local && !split.Contains(copy.To),
            JoinStore store => store.Local == local && plans.Values.OfType<JoinCopy>()
                .Any(copy => copy.To == local && !split.Contains(copy.From)),
            _ => false,
        };
    }

    // The instance fields of a sequential struct, when each is a primitive or a
    // reference a local can hold, and there are few enough of them, and whether
    // the struct declares its size.
    private ImmutableArray<Field>? Fields(CliTypeIdentity type, out bool declaredSize)
    {
        declaredSize = false;
        if (!type.IsValueType || type.Shape is not (CliTypeShape.Named or CliTypeShape.GenericInstantiation) ||
            type.ContainsGenericParameters)
        {
            return null;
        }
        // A v128 value holds Vector4, Quaternion and Plane whole.
        if (type.StackKind == CliValueKind.V128) return null;
        var definition = _typeDefinitions.ResolveTypeIdentity(type);
        declaredSize = definition.DeclaredSize != 0;
        // Vector types lower to SIMD values.
        if (definition.IsEnum || definition.LayoutKind == CliTypeLayoutKind.Explicit ||
            definition.InlineArrayLength != 0 ||
            definition.Namespace is "System.Runtime.Intrinsics" ||
            definition.Namespace == "System.Numerics" && definition.Name.StartsWith("Vector", StringComparison.Ordinal) ||
            definition.Namespace == "System" && definition.Name is "TypedReference" or "ArgIterator" or "RuntimeArgumentHandle")
        {
            return null;
        }
        var typeArguments = type.Shape == CliTypeShape.GenericInstantiation ? type.TypeArguments : [];
        var result = ImmutableArray.CreateBuilder<Field>();
        foreach (var key in definition.Fields)
        {
            var field = _fields.GetField(key);
            if (field.IsStatic || field.IsLiteral) continue;
            var fieldType = field.SignatureType.Substitute(typeArguments, []);
            if (!IsScalar(fieldType) || result.Count == MaximumFields) return null;
            result.Add(new(new(field, type, fieldType)));
        }
        return result.Count == 0 ? null : result.ToImmutable();
    }

    // A byref field, as a span's reference, becomes a byref local, which the
    // root maps report as they report any other. A generic parameter's kind is
    // unknown, and a modified field keeps its memory semantics.
    private static bool IsScalar(CliTypeIdentity type) =>
        type.StackKind is CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8 or
            CliValueKind.NativeInt or CliValueKind.ManagedReference or CliValueKind.ManagedAddress or CliValueKind.V128 &&
        type.Shape != CliTypeShape.Modified;

    private static Field? FieldOf(ImmutableArray<Field> fields, CilOperand operand)
    {
        var key = CilOperands.FieldKey(operand);
        return fields.FirstOrDefault(field => field.Instance.Definition.Key == key);
    }

    private static bool Operates(CilInstruction instruction, CliTypeIdentity type) =>
        CilOperands.Type(instruction.Operand).Equals(type);

    // Whether the instruction only pushes a value or stores another local, so
    // the local keeps the value an earlier load of it read.
    private static bool LeavesLocal(CilInstruction instruction, int local) => instruction switch
    {
        { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var stored } } => stored != local,
        _ => instruction.Operation is CilOperation.Nop or CilOperation.LoadLocal or CilOperation.LoadArgument or
            CilOperation.LoadInt32 or CilOperation.LoadInt64 or CilOperation.LoadFloat32 or CilOperation.LoadFloat64 or
            CilOperation.LoadNull,
    };

    // Whether the instruction reads a whole value of the type from memory, unprefixed: an ldobj or ldelem, which follows
    // the address or array it reads, or an ldfld of a field of the type through an object or an address (not from a
    // struct value, whose field has no address), as a resumed suspension restores an awaited value task from its frame.
    private static bool IsMemorySource(
        ImmutableArray<CilInstruction> instructions,
        int index,
        CliTypeIdentity type,
        ImmutableArray<CliValueKind> entryStack) =>
        (instructions[index].Operation is CilOperation.LoadObject or CilOperation.LoadArrayElement &&
                Operates(instructions[index], type) ||
            instructions[index] is { Operation: CilOperation.LoadField, Operand: CilOperand.FieldInstance { Value: var field } } &&
                !field.Definition.IsStatic && field.FieldType.Equals(type) &&
                entryStack.Length > 0 && IsAddressOrObject(entryStack[^1]) ||
            // A static field's value, read through the address ldsflda takes.
            instructions[index] is { Operation: CilOperation.LoadStaticField, Operand: CilOperand.FieldInstance { Value: var staticField } } &&
                staticField.FieldType.Equals(type)) &&
        // A volatile or unaligned prefix keeps the access whole; the method's first instruction has none.
        (index == 0 || instructions[index - 1].Operation is not (CilOperation.Volatile or CilOperation.Unaligned));

    // The candidates whose address, or the address of one of their fields, is still on the stack where control leaves a
    // block for another: a consumer past a branch target is one the walk back from it cannot find (Roslyn's `x?.M()` on a
    // nullable local dups the address, then reads HasValue, pops one copy on the null path and calls through the other on
    // the value path), so counting the consumers it does find cannot prove every copy reaches a planned access.
    private static IEnumerable<int> AddressesLeavingBlocks(ControlFlowGraph graph, ImmutableArray<CilInstruction> instructions,
        ImmutableDictionary<int, ImmutableArray<CliValueKind>> entryStacks, StackSlotProducers producers,
        Dictionary<int, int> addresses, Dictionary<int, FieldAddress> fieldAddresses)
    {
        var indices = new Dictionary<int, int>(instructions.Length);
        for (var index = 0; index < instructions.Length; index++) indices[instructions[index].Offset] = index;
        foreach (var block in graph.Blocks)
        {
            var successors = graph.Successors[block.Index];
            if (successors.IsDefaultOrEmpty || !indices.TryGetValue(block.Terminator.Offset, out var last) ||
                !entryStacks.TryGetValue(block.Terminator.Offset, out var entry))
            {
                continue;
            }
            var exitDepth = successors
                .Select(successor => entryStacks.TryGetValue(graph.Blocks[successor].StartOffset, out var stack) ? stack.Length : 0)
                .DefaultIfEmpty(0)
                .Max();
            if (exitDepth == 0) continue;
            // The slots below the operands the last instruction takes are the ones it leaves untouched; one it pushed
            // itself is its own. For a call, whose effect its method decides, every slot it could leave is searched,
            // which can only reject more.
            var effect = CilStackEffects.Of(instructions[last]);
            var untouched = effect is { } known ? entry.Length - known.Pops : Math.Min(entry.Length, exitDepth);
            for (var slot = 0; slot < exitDepth; slot++)
            {
                var producer = slot < untouched
                    ? producers.Find(last, slot, duplicates: true)
                    : effect is { Pushes: > 0 } ? last : null;
                if (producer is not { } at) continue;
                if (addresses.TryGetValue(at, out var local)) yield return local;
                else if (fieldAddresses.TryGetValue(at, out var field)) yield return field.Local;
            }
        }
    }

    private static bool IsAddressOrObject(CliValueKind kind) =>
        kind is CliValueKind.ManagedReference or CliValueKind.ManagedAddress or CliValueKind.NativeInt;

    // Where a whole store of a value of the type puts it, when that is a field of the type (the field, through an
    // object or an address on the stack below the value) or the address an stobj of the type writes (no field).
    private static Destination? WholeDestination(CilInstruction consumer, CliTypeIdentity type) =>
        consumer switch
        {
            { Operation: CilOperation.StoreField, Operand: CilOperand.FieldInstance { Value: var field } }
                when !field.Definition.IsStatic && field.FieldType.Equals(type) => new(field),
            { Operation: CilOperation.StoreObject } when Operates(consumer, type) => new(null),
            _ => null,
        };

    private sealed record Destination(FieldInstanceModel? Field);

    // Whether an indirect load, store or initobj reads, writes or clears
    // exactly a field of the given type, as an access to its own local would.
    private static bool AccessesField(CilInstruction instruction, CliTypeIdentity field) =>
        instruction.Operation is CilOperation.LoadObject or CilOperation.StoreObject or CilOperation.InitializeObject &&
        CilOperands.Type(instruction.Operand) is var type &&
        field.StackKind switch
        {
            CliValueKind.ManagedReference => type.StackKind == CliValueKind.ManagedReference,
            // A vector field is read or written whole as any vector of its width.
            CliValueKind.V128 => type.StackKind == CliValueKind.V128 && SimdVectorStorage.Width(type) == SimdVectorStorage.Width(field),
            _ => LocalAddressDereferenceFolder.Width(type) is { } width && width == LocalAddressDereferenceFolder.Width(field),
        };

    // The lane of a System.Numerics vector field that an ldfld, stfld or ldflda through the field's address takes.
    private Field? VectorLane(CilInstruction instruction, CliTypeIdentity vector)
    {
        if (instruction.Operation is not (CilOperation.LoadField or CilOperation.StoreField or CilOperation.LoadFieldAddress) ||
            _lanes.Lane(instruction.Operand, vector) is null)
        {
            return null;
        }
        var definition = instruction.Operand is CilOperand.FieldInstance { Value: var field }
            ? field.Definition
            : _fields.GetField(CilOperands.FieldKey(instruction.Operand));
        return new(new(definition, vector, CliTypeIdentity.Primitive("f4", CliValueKind.F4)));
    }

    // What a store to the field does to its value beyond a store to a local of
    // the field's type: truncate it to the field's width. The typed stack holds
    // a value of the field's own kind, or an address a native int field takes,
    // which keeps the struct whole: null.
    private static ImmutableArray<CilInstruction>? StoreConversion(CliTypeIdentity field, CliValueKind value)
    {
        if (value != field.StackKind) return null;
        var narrowing = field.CanonicalName switch
        {
            "primitive:bool" or "primitive:u1" => (8, true),
            "primitive:i1" => (8, false),
            "primitive:char" or "primitive:u2" => (16, true),
            "primitive:i2" => (16, false),
            _ => ((int Bits, bool Unsigned)?)null,
        };
        return narrowing is { } width
            ? [Synthetic(CilOperation.ConvertNumeric, new CilOperand.NumericConversion(width.Bits, width.Unsigned, false, false, false))]
            : [];
    }

    private static CilInstruction Synthetic(CilOperation operation, CilOperand operand) => new(0, 1, operation, operand);

    // A split local pushes each of its fields where it pushed the whole value,
    // so the stack can grow past the depth the body declared.

    private CilMethodBody Splice(
        CilMethodBody body,
        Dictionary<int, ImmutableArray<Field>> layouts,
        HashSet<int> split,
        Dictionary<int, Plan> plans,
        HashSet<int> callResultStores)
    {
        var locals = body.Locals.ToBuilder();
        var localTypes = body.LocalSignatureTypes.ToBuilder();
        var fieldLocals = new Dictionary<int, ImmutableArray<int>>();
        var temporaries = new Dictionary<int, int>();
        foreach (var local in split.Order())
        {
            fieldLocals[local] = [.. layouts[local].Select(field => Add(field.Type))];
        }
        int Add(CliTypeIdentity type)
        {
            locals.Add(type.StackKind);
            localTypes.Add(type);
            return localTypes.Count - 1;
        }
        int Temporary(int local) => temporaries.TryGetValue(local, out var temporary)
            ? temporary
            : temporaries[local] = Add(body.LocalSignatureTypes[local]);

        var loadedFields = new Dictionary<int, ImmutableArray<(Field Field, int Local)>>();
        var replacements = new List<CilInstruction>[body.Instructions.Length];
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var instruction = body.Instructions[index];
            var replacement = replacements[index] = [];
            if (!plans.TryGetValue(index, out var plan) || Effective(plan) is not { } effective)
            {
                replacement.Add(instruction);
                continue;
            }
            plan = effective;
            switch (plan)
            {
                case Removed:
                    replacement.Add(instruction with { Operation = CilOperation.Nop, Operand = new CilOperand.None() });
                    break;
                case LoadField load:
                    replacement.Add(Local(CilOperation.LoadLocal, FieldLocal(load.Local, load.Field)));
                    break;
                case FieldAddress address:
                    replacement.Add(Local(CilOperation.LoadLocalAddress, FieldLocal(address.Local, address.Field)));
                    break;
                case StoreField store:
                    replacement.AddRange(store.Conversion);
                    replacement.Add(Local(CilOperation.StoreLocal, FieldLocal(store.Local, store.Field)));
                    break;
                case Clear clear:
                    foreach (var (field, local) in Pairs(clear.Local))
                    {
                        replacement.AddRange(Zero(field.Type));
                        replacement.Add(Local(CilOperation.StoreLocal, local));
                    }
                    break;
                case CopySource:
                case JoinStore:
                    replacement.Add(instruction with { Operation = CilOperation.Nop, Operand = new CilOperand.None() });
                    break;
                // A copy field by field: at a copy's store, or where a join's path loaded its value.
                case FieldCopy copy:
                    foreach (var (from, to) in fieldLocals[copy.From].Zip(fieldLocals[copy.To]))
                    {
                        replacement.Add(Local(CilOperation.LoadLocal, from));
                        replacement.Add(Local(CilOperation.StoreLocal, to));
                    }
                    break;
                case LoadWhole load:
                    {
                        var argument = ArgumentOfCall(body.Instructions, index, body.LocalSignatureTypes[load.Local]);
                        // A call's argument gets a temporary of its own, built here and read only by the call.
                        var temporary = argument ? Add(body.LocalSignatureTypes[load.Local]) : Temporary(load.Local);
                        if (argument || index + 1 < body.Instructions.Length && body.Instructions[index + 1].Operation == CilOperation.Return)
                        {
                            replacement.Add(Local(CilOperation.LoadLocalAddress, temporary));
                            replacement.Add(Synthetic(CilOperation.InitializeObject, new CilOperand.TypeIdentity(body.LocalSignatureTypes[load.Local])));
                        }
                        foreach (var (field, local) in Pairs(load.Local))
                        {
                            replacement.Add(Local(CilOperation.LoadLocalAddress, temporary));
                            replacement.Add(Local(CilOperation.LoadLocal, local));
                            replacement.Add(Synthetic(CilOperation.StoreField, new CilOperand.FieldInstance(field.Instance)));
                        }
                        replacement.Add(Local(CilOperation.LoadLocal, temporary));
                        break;
                    }
                case SourceAddress:
                    // The element's address replaces its value: ldelema, the
                    // field's ldflda, or the address ldobj would have read.
                    replacement.Add(instruction.Operation switch
                    {
                        CilOperation.LoadArrayElement => instruction with { Operation = CilOperation.LoadArrayElementAddress },
                        CilOperation.LoadField => instruction with { Operation = CilOperation.LoadFieldAddress },
                        CilOperation.LoadStaticField => instruction with { Operation = CilOperation.LoadStaticFieldAddress },
                        _ => instruction with { Operation = CilOperation.Nop, Operand = new CilOperand.None() },
                    });
                    break;
                case LoadInto:
                    replacement.Add(instruction with { Operation = CilOperation.Nop, Operand = new CilOperand.None() });
                    break;
                case StoreInto store:
                    {
                        // The destination's address, then each field stored through it.
                        if (store.Field is { } destination)
                        {
                            replacement.Add(Synthetic(CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(destination)));
                        }
                        var pairs = Pairs(store.Local).ToArray();
                        for (var field = 0; field < pairs.Length; field++)
                        {
                            if (field < pairs.Length - 1)
                            {
                                replacement.Add(Synthetic(CilOperation.Duplicate, new CilOperand.None()));
                            }
                            replacement.Add(Local(CilOperation.LoadLocal, pairs[field].Local));
                            replacement.Add(Synthetic(CilOperation.StoreField, new CilOperand.FieldInstance(pairs[field].Field.Instance)));
                        }
                        break;
                    }
                case StoreFromSource store:
                    {
                        var loaded = Loaded(store.Local);
                        for (var field = 0; field < loaded.Length; field++)
                        {
                            if (field < loaded.Length - 1)
                            {
                                replacement.Add(Synthetic(CilOperation.Duplicate, new CilOperand.None()));
                            }
                            replacement.Add(Synthetic(CilOperation.LoadField, new CilOperand.FieldInstance(loaded[field].Field.Instance)));
                            replacement.Add(Local(CilOperation.StoreLocal, loaded[field].Local));
                        }
                        break;
                    }
                case StoreWhole store:
                    {
                        // A call's result gets a temporary of its own, read only here.
                        var temporary = callResultStores.Contains(index) ? Add(body.LocalSignatureTypes[store.Local]) : Temporary(store.Local);
                        replacement.Add(Local(CilOperation.StoreLocal, temporary));
                        foreach (var (field, local) in Pairs(store.Local))
                        {
                            replacement.Add(Local(CilOperation.LoadLocalAddress, temporary));
                            replacement.Add(Synthetic(CilOperation.LoadField, new CilOperand.FieldInstance(field.Instance)));
                            replacement.Add(Local(CilOperation.StoreLocal, local));
                        }
                        break;
                    }
            }
        }

        var starts = new Dictionary<int, int>();
        var count = 0;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            starts.Add(body.Instructions[index].Offset, count);
            count += replacements[index].Count;
        }
        starts.Add(body.Instructions[^1].NextOffset, count);
        var rewritten = ImmutableArray.CreateBuilder<CilInstruction>(count);
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var source = body.Instructions[index].SourceOffset;
            foreach (var instruction in replacements[index])
            {
                rewritten.Add(new CilInstruction(rewritten.Count, rewritten.Count + 1, instruction.Operation,
                    RenumberedInstructions.Operand(instruction.Operand, starts))
                {
                    OriginalOffset = source,
                });
            }
        }
        return body with
        {
            Instructions = rewritten.MoveToImmutable(),
            Locals = locals.ToImmutable(),
            LocalSignatureTypes = localTypes.ToImmutable(),
            ExceptionRegions = [.. body.ExceptionRegions.Select(region => RenumberedInstructions.Region(region, starts))],
        };

        // What a plan does once the split locals are known, or null to keep
        // the instruction. A copy is field by field only when both sides are
        // split; otherwise each side is the whole load or store of its own local.
        Plan? Effective(Plan plan) => plan switch
        {
            CopySource copy when split.Contains(copy.To) && split.Contains(copy.From) => copy,
            CopySource copy => split.Contains(copy.From) ? new LoadWhole(copy.From) : null,
            CopyTarget copy when split.Contains(copy.To) && split.Contains(copy.From) => copy,
            CopyTarget copy => split.Contains(copy.To) ? new StoreWhole(copy.To) : null,
            JoinCopy copy when split.Contains(copy.To) => copy,
            JoinCopy copy => split.Contains(copy.From) ? new LoadWhole(copy.From) : null,
            _ => split.Contains(plan.Local) ? plan : null,
        };
        int FieldLocal(int local, Field field) => fieldLocals[local][layouts[local].IndexOf(field)];
        // The fields a whole store from memory loads: those the local's reads
        // need, every field where its whole value is read, and at least one so
        // the source's null check stays.
        ImmutableArray<(Field Field, int Local)> Loaded(int local) =>
            loadedFields.TryGetValue(local, out var cached) ? cached : loadedFields[local] = LoadedFields(local);
        ImmutableArray<(Field Field, int Local)> LoadedFields(int local)
        {
            var effective = plans.Values.Select(Effective).OfType<Plan>().ToArray();
            if (effective.Any(plan => plan is LoadWhole { Local: var whole } && whole == local ||
                    plan is LoadInto { Local: var into } && into == local ||
                    plan is CopySource { From: var from } && from == local ||
                    plan is JoinCopy { From: var joined } && joined == local))
            {
                return [.. Pairs(local)];
            }
            var read = effective
                .Select(plan => plan switch
                {
                    LoadField load when load.Local == local => load.Field,
                    FieldAddress address when address.Local == local => address.Field,
                    _ => null,
                })
                .OfType<Field>()
                .ToHashSet();
            var pairs = Pairs(local).Where(pair => read.Contains(pair.Field)).ToImmutableArray();
            return pairs.IsEmpty ? [Pairs(local).First()] : pairs;
        }
        IEnumerable<(Field Field, int Local)> Pairs(int local) => layouts[local].Zip(fieldLocals[local]);
    }

    private static CilInstruction Local(CilOperation operation, int local) =>
        Synthetic(operation, new CilOperand.Index(local));

    private static ImmutableArray<CilInstruction> Zero(CliTypeIdentity type) => type.StackKind switch
    {
        CliValueKind.I8 => [Synthetic(CilOperation.LoadInt64, new CilOperand.ConstantI8(0))],
        CliValueKind.F4 => [Synthetic(CilOperation.LoadFloat32, new CilOperand.ConstantF4(0))],
        CliValueKind.F8 => [Synthetic(CilOperation.LoadFloat64, new CilOperand.ConstantF8(0))],
        // A null byref is the address zero, as for a native int.
        CliValueKind.NativeInt or CliValueKind.ManagedAddress =>
        [
            Synthetic(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            Synthetic(CilOperation.ConvertNativeInt, new CilOperand.None()),
        ],
        CliValueKind.ManagedReference => [Synthetic(CilOperation.LoadNull, new CilOperand.None())],
        CliValueKind.V128 => [Synthetic(CilOperation.DefaultValue, new CilOperand.TypeIdentity(type))],
        _ => [Synthetic(CilOperation.LoadInt32, new CilOperand.ConstantI4(0))],
    };

    private sealed record Field(FieldInstanceModel Instance)
    {
        public CliTypeIdentity Type => Instance.FieldType;
    }

    private abstract record Plan(int Local);

    private sealed record Removed(int Local) : Plan(Local);

    private sealed record LoadField(int Local, Field Field) : Plan(Local);

    // The field's address becomes the address of its local.
    private sealed record FieldAddress(int Local, Field Field) : Plan(Local);

    private sealed record StoreField(int Local, Field Field, ImmutableArray<CilInstruction> Conversion) : Plan(Local);

    private sealed record Clear(int Local) : Plan(Local);

    private sealed record LoadWhole(int Local) : Plan(Local);

    private sealed record StoreWhole(int Local) : Plan(Local);

    // The ldobj or ldelem whose value a whole store takes, which now leaves
    // the value's address for the store to read fields from.
    private sealed record SourceAddress(int Local) : Plan(Local);

    private sealed record StoreFromSource(int Local) : Plan(Local);

    // A whole load that a store of its own type takes, which now pushes nothing.
    private sealed record LoadInto(int Local) : Plan(Local);

    // That store: each field into the destination field's address (or the stobj's address when Field is null).
    private sealed record StoreInto(int Local, FieldInstanceModel? Field) : Plan(Local);

    private sealed record CopySource(int From, int To) : Plan(From);

    // A plan that copies From's fields into To's where it stands.
    private abstract record FieldCopy(int From, int To, int Local) : Plan(Local);

    private sealed record CopyTarget(int From, int To) : FieldCopy(From, To, To);

    // A whole load of From whose value reaches To's store only through a join.
    private sealed record JoinCopy(int From, int To) : FieldCopy(From, To, From);

    // The join's store into To, which the copies on its paths have made.
    private sealed record JoinStore(int To) : Plan(To);
}
