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
// never read again, which is the only way its padding could be seen. .NET's
// JIT promotes struct locals the same way.
internal sealed class StructLocalScalarReplacer(
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IControlFlowNaturalLoopAnalyzer loops,
    ITypeDefinitionResolver typeDefinitions,
    IFieldRepository fields,
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

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled) return body;
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
            Plan? plan = (address, instruction.Operation) switch
            {
                (_, CilOperation.LoadField) when FieldOf(localFields, instruction.Operand) is { } field =>
                    new LoadField(local, field),
                (true, CilOperation.LoadFieldAddress) when FieldOf(localFields, instruction.Operand) is { } field =>
                    new FieldAddress(local, field),
                (true, CilOperation.StoreField) when FieldOf(localFields, instruction.Operand) is { } field &&
                    StoreConversion(field.Type, stack[^1]) is { } conversion =>
                    new StoreField(local, field, conversion),
                (true, CilOperation.InitializeObject) when Operates(instruction, type) => new Clear(local),
                (true, CilOperation.LoadObject) when Operates(instruction, type) => new LoadWhole(local),
                (true, CilOperation.StoreObject) when Operates(instruction, type) => new StoreWhole(local),
                (true, CilOperation.Pop) => new Removed(local),
                _ => null,
            };
            if (plan is null) continue;
            plans[producer] = new Removed(local);
            plans[index] = plan;
            if (address) consumed[producer] = consumed.GetValueOrDefault(producer) + 1;
            if (plan is FieldAddress pushed) fieldAddresses[index] = pushed;
            if (plan is LoadField or StoreField or FieldAddress) accessed.Add(local);
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
                    plans[index] = new LoadWhole(local);
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
                    else if (index > 0 && !plans.ContainsKey(index - 1) && !producers.Enters(index) &&
                        producers.FindTop(index) == index - 1 &&
                        IsMemorySource(instructions, index - 1, body.LocalSignatureTypes[local]))
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
        // A struct of declared size keeps bytes beyond its fields, which only
        // a whole read of the value could expose.
        rejected.UnionWith(sized.Where(local => plans.Values.Any(plan => plan switch
        {
            LoadWhole load => load.Local == local,
            CopySource copy => copy.From == local || copy.To == local,
            CopyTarget copy => copy.From == local || copy.To == local,
            _ => false,
        })));
        var split = layouts.Keys.Where(local => accessed.Contains(local) && !rejected.Contains(local)).ToHashSet();
        if (split.Count == 0) return body;
        KeepAmortizedTransfers(graph, instructions, plans, split);
        return split.Count == 0 ? body : Splice(body, layouts, split, plans);

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

    // A whole load or store through the temporary copies the struct and every
    // field, so it pays off only where the fields are accessed in a loop it
    // sits outside of, as an enumerator assigned before the loop that moves it.
    // A struct copied out of an array on every iteration stays whole. Leaving
    // one local whole turns its copies with others into such transfers.
    private void KeepAmortizedTransfers(
        ControlFlowGraph graph,
        ImmutableArray<CilInstruction> instructions,
        Dictionary<int, Plan> plans,
        HashSet<int> split)
    {
        var blocks = graph.Blocks
            .SelectMany(block => block.Instructions.Select(instruction => (instruction.Offset, block.Index)))
            .ToDictionary(pair => pair.Offset, pair => pair.Index);
        var loops = _loops.Analyze(graph, graph.Entry.Index, graph.ReachableBlocks);
        int Depth(int index) => blocks.TryGetValue(instructions[index].Offset, out var block)
            ? loops.Count(loop => loop.Contains(block))
            : 0;
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
                if (plans.Any(pair => Transfers(pair.Value, local) && Depth(pair.Key) >= deepestAccess))
                {
                    split.Remove(local);
                    changed = true;
                }
            }
        }
        while (changed);

        bool Transfers(Plan plan, int local) => plan switch
        {
            LoadWhole or StoreWhole => plan.Local == local,
            CopySource copy => copy.From == local && !split.Contains(copy.To),
            CopyTarget copy => copy.To == local && !split.Contains(copy.From),
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

    private static bool IsScalar(CliTypeIdentity type) =>
        type.StackKind is CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8 or
            CliValueKind.NativeInt or CliValueKind.ManagedReference &&
        type.Shape is not (CliTypeShape.ManagedByReference or CliTypeShape.GenericTypeParameter or
            CliTypeShape.GenericMethodParameter or CliTypeShape.Modified);

    private static Field? FieldOf(ImmutableArray<Field> fields, CilOperand operand)
    {
        var key = operand switch
        {
            CilOperand.FieldInstance instance => instance.Value.Definition.Key,
            CilOperand.Entity entity => entity.Key,
            _ => (EntityKey?)null,
        };
        return key is null ? null : fields.FirstOrDefault(field => field.Instance.Definition.Key == key);
    }

    private static bool Operates(CilInstruction instruction, CliTypeIdentity type) =>
        instruction.Operand is CilOperand.TypeIdentity { Value: var operand } && operand.Equals(type);

    // Whether the instruction only pushes a value or stores another local, so
    // the local keeps the value an earlier load of it read.
    private static bool LeavesLocal(CilInstruction instruction, int local) => instruction switch
    {
        { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var stored } } => stored != local,
        _ => instruction.Operation is CilOperation.Nop or CilOperation.LoadLocal or CilOperation.LoadArgument or
            CilOperation.LoadInt32 or CilOperation.LoadInt64 or CilOperation.LoadFloat32 or CilOperation.LoadFloat64 or
            CilOperation.LoadNull,
    };

    // Whether the instruction reads a whole value of the type from memory, an
    // unprefixed ldobj or ldelem.
    private static bool IsMemorySource(ImmutableArray<CilInstruction> instructions, int index, CliTypeIdentity type) =>
        instructions[index].Operation is CilOperation.LoadObject or CilOperation.LoadArrayElement &&
        Operates(instructions[index], type) &&
        (index == 0 || instructions[index - 1].Operation is not (CilOperation.Volatile or CilOperation.Unaligned));

    // Whether an indirect load, store or initobj reads, writes or clears
    // exactly a field of the given type, as an access to its own local would.
    private static bool AccessesField(CilInstruction instruction, CliTypeIdentity field) =>
        instruction.Operation is CilOperation.LoadObject or CilOperation.StoreObject or CilOperation.InitializeObject &&
        instruction.Operand is CilOperand.TypeIdentity { Value: var type } &&
        (field.StackKind == CliValueKind.ManagedReference
            ? type.StackKind == CliValueKind.ManagedReference
            : LocalAddressDereferenceFolder.Width(type) is { } width &&
                width == LocalAddressDereferenceFolder.Width(field));

    // What a store to the field does to its value beyond a store to a local of
    // the field's type: truncate it to the field's width, or round it to its
    // precision. Null when the stack value is of another kind.
    private static ImmutableArray<CilInstruction>? StoreConversion(CliTypeIdentity field, CliValueKind value)
    {
        var narrowing = field.CanonicalName switch
        {
            "primitive:bool" or "primitive:u1" => (8, true),
            "primitive:i1" => (8, false),
            "primitive:char" or "primitive:u2" => (16, true),
            "primitive:i2" => (16, false),
            _ => ((int Bits, bool Unsigned)?)null,
        };
        if (narrowing is { } width)
        {
            return value == CliValueKind.I4
                ? [Synthetic(CilOperation.ConvertNumeric, new CilOperand.NumericConversion(width.Bits, width.Unsigned, false, false, false))]
                : null;
        }
        return (field.StackKind, value) switch
        {
            (CliValueKind.F4, CliValueKind.F8) => [Synthetic(CilOperation.ConvertFloat32, new CilOperand.None())],
            (CliValueKind.F8, CliValueKind.F4) => [Synthetic(CilOperation.ConvertFloat64, new CilOperand.None())],
            var (kind, stackKind) when kind == stackKind => [],
            _ => null,
        };
    }

    private static CilInstruction Synthetic(CilOperation operation, CilOperand operand) => new(0, 1, operation, operand);

    private static CilMethodBody Splice(
        CilMethodBody body,
        Dictionary<int, ImmutableArray<Field>> layouts,
        HashSet<int> split,
        Dictionary<int, Plan> plans)
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
                    replacement.Add(instruction with { Operation = CilOperation.Nop, Operand = new CilOperand.None() });
                    break;
                case CopyTarget copy:
                    foreach (var (from, to) in fieldLocals[copy.From].Zip(fieldLocals[copy.To]))
                    {
                        replacement.Add(Local(CilOperation.LoadLocal, from));
                        replacement.Add(Local(CilOperation.StoreLocal, to));
                    }
                    break;
                case LoadWhole load:
                    {
                        var temporary = Temporary(load.Local);
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
                    // The element's address replaces its value: ldelema, or
                    // the address ldobj would have read.
                    replacement.Add(instruction.Operation == CilOperation.LoadArrayElement
                        ? instruction with { Operation = CilOperation.LoadArrayElementAddress }
                        : instruction with { Operation = CilOperation.Nop, Operand = new CilOperand.None() });
                    break;
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
                        var temporary = Temporary(store.Local);
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
                    plan is CopySource { From: var from } && from == local))
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
        CliValueKind.NativeInt =>
        [
            Synthetic(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            Synthetic(CilOperation.ConvertNativeInt, new CilOperand.None()),
        ],
        CliValueKind.ManagedReference => [Synthetic(CilOperation.LoadNull, new CilOperand.None())],
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

    private sealed record CopySource(int From, int To) : Plan(From);

    private sealed record CopyTarget(int From, int To) : Plan(To);
}
