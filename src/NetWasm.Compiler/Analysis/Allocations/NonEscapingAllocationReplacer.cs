using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Allocations;

internal interface INonEscapingAllocationReplacer
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// An object a method allocates and reaches only through its locals, to read
// and write its fields and test it for null, needs no allocation: each field
// becomes a local, as .NET's JIT promotes the fields of an object it allocates
// on the stack. foreach over an array seen as IEnumerable<T> allocates such an
// enumerator once its calls are direct and inlined.
//
// The constructor must only store arguments and constants into the object,
// after a base constructor that does the same, and the class must have no
// finalizer. No constructor in the chain may trigger type initialization: a
// type without beforefieldinit runs its static constructor when an instance
// is constructed, and that must still happen. Roslyn reuses one local for the enumerators of successive loops
// and inlining copies the receiver into a local per call, so every load must
// see the newest object: an allocation makes its local fresh and every other
// stale, a copy carries freshness, and a load of a stale local keeps the
// allocations. Field locals then always hold the newest object's fields.
internal sealed class NonEscapingAllocationReplacer(
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IMethodBodyReader methodBodies,
    ICalledMethodResolver calledMethods,
    IFieldRepository fields,
    IFinalizerResolver finalizers,
    IMethodInitializationTriggerClassifier initializationTriggers,
    IStackValueConsumerFinder consumers,
    bool enabled) : INonEscapingAllocationReplacer
{
    // Constructor chains are short; deeper ones stay allocations.
    private const int MaximumConstructorDepth = 8;
    private const int Stale = -2;
    private const int Unreached = -1;
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly IMethodBodyReader _methodBodies = methodBodies ?? throw new ArgumentNullException(nameof(methodBodies));
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly IFinalizerResolver _finalizers = finalizers ?? throw new ArgumentNullException(nameof(finalizers));
    private readonly IMethodInitializationTriggerClassifier _initializationTriggers = initializationTriggers ??
        throw new ArgumentNullException(nameof(initializationTriggers));
    private readonly IStackValueConsumerFinder _consumers = consumers ?? throw new ArgumentNullException(nameof(consumers));
    private readonly object _finalizerLock = new();

    // What a constructor leaves in a field: an argument, or a constant.
    private sealed record FieldStore(EntityKey Field, CliTypeIdentity Type, int? Argument, CilInstruction? Constant);

    // A newobj stored straight into a local.
    private sealed record Site(int Index, int Local, CliTypeIdentity Type, MethodInstanceModel Constructor,
        ImmutableArray<FieldStore> Stores);

    private enum UseKind { Read, Write, Copy, Test, Drop }

    private sealed record Use(int Load, int Consumer, UseKind Kind, EntityKey? Field);

    private sealed record Group(ImmutableArray<Site> Sites, ImmutableArray<Use> Uses,
        ImmutableDictionary<EntityKey, CliTypeIdentity> Fields);

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled || !body.Instructions.Any(static instruction => instruction.Operation == CilOperation.NewObject))
        {
            return body;
        }
        var entries = InstructionEntries.Of(body);
        var sites = Sites(body, entries);
        if (sites.Count == 0)
        {
            return body;
        }
        var entryStacks = _stacks.Validate(_graphs.Build(body)).InstructionEntryStacks;
        var producers = new StackSlotProducers(body, entryStacks);
        var groups = sites
            .GroupBy(static site => site.Type)
            .Select(sites => Plan(body, entryStacks, producers, entries, [.. sites]))
            .OfType<Group>()
            .ToArray();
        return groups.Length == 0 ? body : Apply(body, groups);
    }

    private List<Site> Sites(CilMethodBody body, HashSet<int> entries)
    {
        var sites = new List<Site>();
        for (var index = 0; index + 1 < body.Instructions.Length; index++)
        {
            if (body.Instructions[index].Operation != CilOperation.NewObject ||
                body.Instructions[index + 1] is not { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var local } } ||
                entries.Contains(body.Instructions[index + 1].Offset) ||
                _calledMethods.Resolve(body.Instructions[index]) is not
                {
                    DeclaringType: { IsValueType: false, ContainsGenericParameters: false } type,
                } constructor ||
                type.Shape is not (CliTypeShape.Named or CliTypeShape.GenericInstantiation) ||
                constructor.Signature.ParameterSignatureTypes.Any(static parameter => parameter.ContainsGenericParameters) ||
                Finalizable(type) ||
                Stores(constructor, 0) is not { } stores)
            {
                continue;
            }
            sites.Add(new(index, local, type, constructor, stores));
        }
        return sites;
    }

    private bool Finalizable(CliTypeIdentity type)
    {
        lock (_finalizerLock)
        {
            return _finalizers.Resolve(type) is not null;
        }
    }

    // The fields a constructor sets, in order, when it only stores arguments
    // and constants into the new object after a base constructor that does
    // the same; arguments are numbered from zero, without the object.
    private ImmutableArray<FieldStore>? Stores(MethodInstanceModel constructor, int depth)
    {
        if (depth >= MaximumConstructorDepth || !constructor.Definition.HasManagedBody ||
            _initializationTriggers.Classify(constructor.Definition))
        {
            return null;
        }
        var body = _methodBodies.ReadMethodBody(constructor);
        if (!body.ExceptionRegions.IsEmpty)
        {
            return null;
        }
        // Null for the new object, or what the value is.
        var stack = new Stack<(bool Object, int? Argument, CilInstruction? Constant)>();
        var stores = ImmutableArray.CreateBuilder<FieldStore>();
        foreach (var instruction in body.Instructions)
        {
            switch (instruction)
            {
                case { Operation: CilOperation.Nop }:
                    break;
                case { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: var argument } }:
                    stack.Push(argument == 0 ? (true, null, null) : (false, argument - 1, null));
                    break;
                case
                {
                    Operation: CilOperation.LoadInt32 or CilOperation.LoadInt64 or CilOperation.LoadFloat32 or
                    CilOperation.LoadFloat64 or CilOperation.LoadNull
                }:
                    stack.Push((false, null, instruction));
                    break;
                case { Operation: CilOperation.StoreField } when stack.Count == 2 &&
                    stack.Pop() is { Object: false } value && stack.Pop().Object &&
                    Field(instruction) is var (field, type) && Supported(type):
                    stores.Add(new(field, type, value.Argument, value.Constant));
                    break;
                case { Operation: CilOperation.Call } when
                    _calledMethods.Resolve(instruction) is { Definition: { IsStatic: false, Name: ".ctor" } } chained &&
                    stack.Count == chained.Signature.ParameterTypes.Length + 1 &&
                    stack.Reverse().First().Object && stack.Take(chained.Signature.ParameterTypes.Length).All(static value => !value.Object) &&
                    Stores(chained, depth + 1) is { } chainedStores:
                    var arguments = stack.Reverse().Skip(1).ToArray();
                    stack.Clear();
                    foreach (var store in chainedStores)
                    {
                        stores.Add(store.Argument is { } argument
                            ? store with { Argument = arguments[argument].Argument, Constant = arguments[argument].Constant }
                            : store);
                    }
                    break;
                case { Operation: CilOperation.Return } when stack.Count == 0:
                    return stores.ToImmutable();
                default:
                    return null;
            }
        }
        return null;
    }

    // A field's definition and the type of its value.
    private (EntityKey Field, CliTypeIdentity Type) Field(CilInstruction instruction) => instruction.Operand switch
    {
        CilOperand.FieldInstance { Value: var field } => (field.Definition.Key, field.FieldType),
        CilOperand.Entity { Key: var key } => (key, _fields.GetField(key).SignatureType),
        _ => throw new InvalidOperationException("A field access names its field."),
    };

    // Values a local holds as they are, without the truncation a narrow
    // field's store performs or a struct's copy.
    private static bool Supported(CliTypeIdentity type) =>
        !type.ContainsGenericParameters &&
        type.StackKind is CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8 or CliValueKind.ManagedReference &&
        type.CanonicalName is not ("primitive:u1" or "primitive:i1" or "primitive:u2" or "primitive:i2" or "primitive:bool" or
            "primitive:char");

    private Group? Plan(
        CilMethodBody body,
        IReadOnlyDictionary<int, ImmutableArray<CliValueKind>> entryStacks,
        StackSlotProducers producers,
        HashSet<int> entries,
        ImmutableArray<Site> sites)
    {
        var instructions = body.Instructions;
        var siteStores = sites.ToDictionary(static site => site.Index + 1);
        // The locals that hold the objects: the sites' locals and their copies.
        var members = sites.Select(static site => site.Local).ToHashSet();
        for (var grown = true; grown;)
        {
            grown = false;
            for (var index = 1; index < instructions.Length; index++)
            {
                if (instructions[index] is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var target } } &&
                    !members.Contains(target) && IsCopy(instructions, entries, members, index))
                {
                    members.Add(target);
                    grown = true;
                }
            }
        }

        var uses = ImmutableArray.CreateBuilder<Use>();
        var fieldTypes = new Dictionary<EntityKey, CliTypeIdentity>();
        foreach (var store in sites.SelectMany(static site => site.Stores))
        {
            fieldTypes.TryAdd(store.Field, store.Type);
        }
        for (var index = 0; index < instructions.Length; index++)
        {
            var instruction = instructions[index];
            if (instruction.Operand is not CilOperand.Index { Value: var local } || !members.Contains(local))
            {
                continue;
            }
            switch (instruction.Operation)
            {
                case CilOperation.LoadLocalAddress:
                    return null;
                // Only the sites and copies store into the locals, and not in a
                // handler, whose effects the freshness walk does not follow out.
                case CilOperation.StoreLocal when (siteStores.ContainsKey(index) || IsCopy(instructions, entries, members, index)) &&
                    !InHandler(body, instruction.Offset):
                    continue;
                case CilOperation.StoreLocal:
                    return null;
                case CilOperation.LoadLocal:
                    if (Consumption(body, entryStacks, producers, entries, members, index) is not { } use ||
                        use.Field is { } field && !Record(fieldTypes, field, Field(instructions[use.Consumer]).Type))
                    {
                        return null;
                    }
                    uses.Add(use);
                    continue;
            }
        }
        if (!Fresh(body, sites, members, uses.ToImmutable()))
        {
            return null;
        }
        return new(sites, uses.ToImmutable(), fieldTypes.ToImmutableDictionary());
    }

    // A field's type is recorded once and must be one a local holds as is.
    private static bool Record(Dictionary<EntityKey, CliTypeIdentity> fieldTypes, EntityKey field, CliTypeIdentity type)
    {
        if (!Supported(type))
        {
            return false;
        }
        fieldTypes.TryAdd(field, type);
        return true;
    }

    // ldloc of a member followed by stloc, which nothing else reaches.
    private static bool IsCopy(ImmutableArray<CilInstruction> instructions, HashSet<int> entries, HashSet<int> members, int store) =>
        store > 0 && instructions[store].Operation == CilOperation.StoreLocal && !entries.Contains(instructions[store].Offset) &&
        instructions[store - 1] is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var source } } &&
        members.Contains(source);

    // How the value a member's load pushes is consumed, or null when it escapes.
    private Use? Consumption(
        CilMethodBody body,
        IReadOnlyDictionary<int, ImmutableArray<CliValueKind>> entryStacks,
        StackSlotProducers producers,
        HashSet<int> entries,
        HashSet<int> members,
        int load)
    {
        var instructions = body.Instructions;
        if (_consumers.Find(instructions, entries, load) is not { } consumer ||
            consumer > 0 && instructions[consumer - 1].Operation is CilOperation.Volatile or CilOperation.Unaligned)
        {
            return null;
        }
        return instructions[consumer] switch
        {
            { Operation: CilOperation.LoadField } => new(load, consumer, UseKind.Read, Field(instructions[consumer]).Field),
            { Operation: CilOperation.StoreField } when entryStacks.TryGetValue(instructions[consumer].Offset, out var stack) &&
                producers.Find(consumer, stack.Length - 2) == load =>
                new(load, consumer, UseKind.Write, Field(instructions[consumer]).Field),
            { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var target } }
                when consumer == load + 1 && members.Contains(target) => new(load, consumer, UseKind.Copy, null),
            { Operation: CilOperation.BranchIfTrue or CilOperation.BranchIfFalse } => new(load, consumer, UseKind.Test, null),
            { Operation: CilOperation.Pop } => new(load, consumer, UseKind.Drop, null),
            _ => null,
        };
    }

    private static bool InHandler(CilMethodBody body, int offset) => body.ExceptionRegions.Any(region =>
        (region.FilterOffset ?? region.HandlerOffset) <= offset && offset < region.HandlerOffset + region.HandlerLength);

    // Whether every load sees the newest object. Each member is stale (null,
    // or an older object), fresh from one site, or not yet reached; a handler
    // starts from what any point of its protected region holds.
    private bool Fresh(CilMethodBody body, ImmutableArray<Site> sites, HashSet<int> members, ImmutableArray<Use> uses)
    {
        var graph = _graphs.Build(body);
        var slots = members.Order().Select((local, slot) => (local, slot)).ToDictionary(pair => pair.local, pair => pair.slot);
        var siteStores = sites.Select((site, id) => (site, id)).ToDictionary(pair => pair.site.Index + 1, pair => (pair.site.Local, pair.id));
        var loads = uses.Select(static use => use.Load).ToHashSet();
        var inputs = new Dictionary<int, int[]> { [graph.Entry.Index] = Enumerable.Repeat(Stale, slots.Count).ToArray() };
        var handlers = new Dictionary<int, int[]>();
        var indexOf = body.Instructions.Select((instruction, index) => (instruction.Offset, index))
            .ToDictionary(static pair => pair.Offset, static pair => pair.index);

        bool Walk(bool check)
        {
            var changed = false;
            foreach (var block in graph.Blocks)
            {
                var input = inputs.GetValueOrDefault(block.Index);
                for (var region = 0; region < body.ExceptionRegions.Length; region++)
                {
                    var start = body.ExceptionRegions[region].FilterOffset ?? body.ExceptionRegions[region].HandlerOffset;
                    if (start == block.StartOffset && handlers.TryGetValue(region, out var handler))
                    {
                        input = input is null ? (int[])handler.Clone() : Merge(input, handler);
                    }
                }
                if (input is null)
                {
                    continue;
                }
                var state = (int[])input.Clone();
                foreach (var instruction in block.Instructions)
                {
                    changed |= Expose(body, handlers, instruction.Offset, state);
                    var index = indexOf[instruction.Offset];
                    if (check && loads.Contains(index) &&
                        instruction.Operand is CilOperand.Index { Value: var loaded } && state[slots[loaded]] < 0)
                    {
                        return false;
                    }
                    if (siteStores.TryGetValue(index, out var site))
                    {
                        Array.Fill(state, Stale);
                        state[slots[site.Local]] = site.id;
                    }
                    else if (instruction is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var stored } } &&
                        slots.TryGetValue(stored, out var target))
                    {
                        // A copy: the load before it reads a member.
                        state[target] = state[slots[((CilOperand.Index)body.Instructions[index - 1].Operand).Value]];
                    }
                    changed |= Expose(body, handlers, instruction.NextOffset, state);
                }
                foreach (var successor in graph.Successors[block.Index])
                {
                    var merged = inputs.TryGetValue(successor, out var existing) ? Merge(existing, state) : state;
                    if (existing is null || !merged.SequenceEqual(existing))
                    {
                        inputs[successor] = merged;
                        changed = true;
                    }
                }
            }
            return check || changed;
        }

        // Each walk only lowers states, so the walks end.
        while (Walk(check: false))
        {
        }
        return Walk(check: true);
    }

    // A point inside a protected region can transfer to its handler.
    private static bool Expose(CilMethodBody body, Dictionary<int, int[]> handlers, int offset, int[] state)
    {
        var changed = false;
        for (var region = 0; region < body.ExceptionRegions.Length; region++)
        {
            var protectedRegion = body.ExceptionRegions[region];
            if (offset < protectedRegion.TryOffset || offset >= protectedRegion.TryOffset + protectedRegion.TryLength)
            {
                continue;
            }
            var merged = handlers.TryGetValue(region, out var existing) ? Merge(existing, state) : (int[])state.Clone();
            if (existing is null || !merged.SequenceEqual(existing))
            {
                handlers[region] = merged;
                changed = true;
            }
        }
        return changed;
    }

    private static int[] Merge(int[] left, int[] right) =>
        [.. left.Zip(right, static (a, b) => a == Unreached ? b : b == Unreached ? a : a == b ? a : Stale)];

    private static CilMethodBody Apply(CilMethodBody body, Group[] groups)
    {
        var locals = body.Locals.ToBuilder();
        var localTypes = body.LocalSignatureTypes.ToBuilder();
        int AddLocal(CliTypeIdentity type)
        {
            locals.Add(type.StackKind);
            localTypes.Add(type);
            return localTypes.Count - 1;
        }

        var replaced = new Dictionary<int, ImmutableArray<(CilOperation Operation, CilOperand Operand)>>();
        var nop = ImmutableArray.Create<(CilOperation, CilOperand)>((CilOperation.Nop, new CilOperand.None()));
        foreach (var group in groups)
        {
            var fieldLocals = group.Fields.ToDictionary(static pair => pair.Key, pair => AddLocal(pair.Value));
            foreach (var site in group.Sites)
            {
                // The arguments are on the stack, the last on top.
                var arguments = site.Constructor.Signature.ParameterSignatureTypes.Select(AddLocal).ToArray();
                var code = ImmutableArray.CreateBuilder<(CilOperation, CilOperand)>();
                for (var argument = arguments.Length - 1; argument >= 0; argument--)
                {
                    code.Add((CilOperation.StoreLocal, new CilOperand.Index(arguments[argument])));
                }
                // A new object's fields start at zero.
                foreach (var (field, local) in fieldLocals)
                {
                    code.Add(Zero(group.Fields[field]));
                    code.Add((CilOperation.StoreLocal, new CilOperand.Index(local)));
                }
                foreach (var store in site.Stores)
                {
                    code.Add(store.Argument is { } argument
                        ? (CilOperation.LoadLocal, new CilOperand.Index(arguments[argument]))
                        : (store.Constant!.Operation, store.Constant.Operand));
                    code.Add((CilOperation.StoreLocal, new CilOperand.Index(fieldLocals[store.Field])));
                }
                replaced[site.Index] = code.ToImmutable();
                replaced[site.Index + 1] = nop;
            }
            foreach (var use in group.Uses)
            {
                replaced[use.Load] = nop;
                var consumer = body.Instructions[use.Consumer];
                replaced[use.Consumer] = use.Kind switch
                {
                    UseKind.Read => [(CilOperation.LoadLocal, new CilOperand.Index(fieldLocals[use.Field!.Value]))],
                    UseKind.Write => [(CilOperation.StoreLocal, new CilOperand.Index(fieldLocals[use.Field!.Value]))],
                    // The object is never null: brtrue always branches, brfalse never.
                    UseKind.Test when consumer.Operation == CilOperation.BranchIfTrue => [(CilOperation.Branch, consumer.Operand)],
                    _ => nop,
                };
            }
        }

        var starts = new Dictionary<int, int>();
        var count = 0;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            starts.Add(body.Instructions[index].Offset, count);
            count += replaced.TryGetValue(index, out var replacement) ? replacement.Length : 1;
        }
        starts.Add(body.Instructions[^1].NextOffset, count);

        var rewritten = ImmutableArray.CreateBuilder<CilInstruction>(count);
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var instruction = body.Instructions[index];
            var replacement = replaced.TryGetValue(index, out var code) ? code : [(instruction.Operation, instruction.Operand)];
            foreach (var (operation, operand) in replacement)
            {
                rewritten.Add(new CilInstruction(rewritten.Count, rewritten.Count + 1, operation,
                    RenumberedInstructions.Operand(operand, starts))
                {
                    OriginalOffset = instruction.SourceOffset,
                });
            }
        }
        return body with
        {
            Instructions = rewritten.MoveToImmutable(),
            LocalSignatureTypes = localTypes.ToImmutable(),
            Locals = locals.ToImmutable(),
            ExceptionRegions = [.. body.ExceptionRegions.Select(region => RenumberedInstructions.Region(region, starts))],
        };
    }

    private static (CilOperation, CilOperand) Zero(CliTypeIdentity type) => type.StackKind switch
    {
        CliValueKind.I4 => (CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
        CliValueKind.I8 => (CilOperation.LoadInt64, new CilOperand.ConstantI8(0)),
        CliValueKind.F4 => (CilOperation.LoadFloat32, new CilOperand.ConstantF4(0)),
        CliValueKind.F8 => (CilOperation.LoadFloat64, new CilOperand.ConstantF8(0)),
        _ => (CilOperation.LoadNull, new CilOperand.None()),
    };
}
