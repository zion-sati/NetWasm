using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Devirtualization;

// A local that only ever holds the object one allocation created has that
// allocation's exact class, so a virtual or interface call on it reaches one
// implementation, the way .NET's JIT devirtualizes from an allocation. The call
// becomes direct, and since an allocation is never null it needs no null check,
// so the O3 inliner can take the implementation too.
//
// An array's interface methods do not depend on its element type: a receiver
// whose static type is an SZ array reaches the SZArrayHelper method for the
// interface's type argument, as .NET's JIT devirtualizes a foreach over an
// array seen as IEnumerable<T>. That helper is static and takes the array, so
// the direct call keeps callvirt's null check only when the helper reads the
// array's length before anything else; GetEnumerator and Count do.
internal sealed class ExactReceiverDevirtualizer(
    ICalledMethodResolver calledMethods,
    IDispatchTargetResolver dispatchTargets,
    IMethodBodyReader methodBodies,
    IFieldRepository fields,
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IControlFlowDominanceAnalyzer dominance,
    bool enabled) : IExactReceiverDevirtualizer
{
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));
    private readonly IDispatchTargetResolver _dispatchTargets = dispatchTargets ?? throw new ArgumentNullException(nameof(dispatchTargets));
    private readonly IMethodBodyReader _methodBodies = methodBodies ?? throw new ArgumentNullException(nameof(methodBodies));
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly IControlFlowDominanceAnalyzer _dominance = dominance ?? throw new ArgumentNullException(nameof(dominance));
    private static readonly Dictionary<int, CliTypeIdentity> NoArrayLocals = [];
    // The dispatch resolver caches without locking; methods are optimized in parallel.
    private readonly object _dispatchLock = new();

    public CilMethodBody Rewrite(
        CilMethodBody body,
        IReadOnlyDictionary<int, ImmutableArray<CliValueKind>> knownStacks)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(knownStacks);
        var instructions = body.Instructions;
        if (!enabled ||
            !instructions.Any(static instruction => instruction.Operation == CilOperation.CallVirtual) ||
            !instructions.Any(instruction => instruction.Operation == CilOperation.NewObject || StaticArrayType(body, instruction, NoArrayLocals) is not null))
        {
            return body;
        }

        var graph = _graphs.Build(body);
        var entryStacks = knownStacks.Count != 0 ? knownStacks : _stacks.Validate(graph).InstructionEntryStacks;
        var producers = new StackSlotProducers(body, entryStacks);
        var allocations = ExactLocals(body, producers);
        var arrays = ArrayLocals(body, producers);

        var blocks = new Dictionary<int, int>();
        foreach (var block in graph.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                blocks[instruction.Offset] = block.Index;
            }
        }
        var dominators = _dominance.Analyze(graph, graph.Entry.Index, graph.ReachableBlocks);
        var rewritten = instructions.ToBuilder();
        var changed = false;
        for (var index = 1; index < instructions.Length; index++)
        {
            var call = instructions[index];
            var constrained = instructions[index - 1].Operation == CilOperation.Constrained;
            if (call.Operation != CilOperation.CallVirtual ||
                _calledMethods.Resolve(call) is not { Definition.IsStatic: false } declaration ||
                (constrained
                    ? index < 2 || !IsConstrainedRead(instructions, producers, index - 2) ? null : index - 2
                    : entryStacks.TryGetValue(call.Offset, out var stack)
                        ? producers.Find(index, stack.Length - declaration.Signature.ParameterTypes.Length - 1)
                        : null) is not { } receiver)
            {
                continue;
            }
            // The receiver's exact class, or an array's type, which fixes its interface methods.
            var exact = constrained ? null : StaticArrayType(body, instructions[receiver], arrays);
            if (exact is null &&
                (instructions[receiver] is not { Operand: CilOperand.Index local } receiving ||
                    receiving.Operation != (constrained ? CilOperation.LoadLocalAddress : CilOperation.LoadLocal) ||
                    !allocations.TryGetValue(local.Value, out var allocation) ||
                    !allocation.Stores.Any(store => Dominates(body, blocks, dominators, store, receiver)) ||
                    (exact = allocation.Type) is null))
            {
                continue;
            }
            DispatchTargetModel? target;
            lock (_dispatchLock)
            {
                target = _dispatchTargets.Resolve(declaration, exact);
            }
            if (target?.Method is not { Definition.HasManagedBody: true } implementation ||
                exact.Shape == CliTypeShape.SzArray &&
                (!implementation.Definition.IsStatic || !ReadsTheArrayLengthFirst(_methodBodies.ReadMethodBody(implementation))))
            {
                continue;
            }
            if (constrained)
            {
                // A constrained call on a reference type loads the reference from the address.
                rewritten[receiver] = instructions[receiver] with { Operation = CilOperation.LoadLocal };
                rewritten[index - 1] = instructions[index - 1] with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
            }
            rewritten[index] = call with
            {
                Operation = CilOperation.Call,
                Operand = new CilOperand.MethodInstance(implementation),
            };
            changed = true;
        }
        return changed ? body with { Instructions = rewritten.ToImmutable() } : body;
    }

    // The SZ array type an instruction pushes, from the field, local or method
    // result it reads or the array it creates.
    private CliTypeIdentity? StaticArrayType(
        CilMethodBody body,
        CilInstruction instruction,
        Dictionary<int, CliTypeIdentity> arrayLocals) =>
        (instruction switch
        {
            { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } }
                when arrayLocals.TryGetValue(local, out var stored) => stored,
            { Operation: CilOperation.LoadStaticField or CilOperation.LoadField, Operand: CilOperand.FieldInstance { Value: var field } } =>
                field.FieldType,
            { Operation: CilOperation.LoadStaticField or CilOperation.LoadField, Operand: CilOperand.Entity { Key: var field } } =>
                _fields.GetField(field).SignatureType,
            { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } }
                when (uint)local < (uint)body.LocalSignatureTypes.Length => body.LocalSignatureTypes[local],
            { Operation: CilOperation.NewArray, Operand: CilOperand.TypeIdentity { Value: var element } } =>
                CliTypeIdentity.SzArray(element),
            { Operation: CilOperation.Call, Operand: CilOperand.MethodInstance { Value: var method } } =>
                method.Signature.ReturnSignatureType.Shape == CliTypeShape.SzArray
                    ? method.Signature.ReturnSignatureType
                    : ReturnedArrayType(method),
            { Operation: CilOperation.CallVirtual, Operand: CilOperand.MethodInstance { Value: var method } } =>
                method.Signature.ReturnSignatureType,
            _ => null,
        }) is { Shape: CliTypeShape.SzArray, ContainsGenericParameters: false } type
            ? type
            : null;

    // The SZ array type a directly called method returns, when it is declared
    // to return an interface but each of its returns reads an array field or
    // creates an array, as a property over an array does; a call the inliner
    // keeps, because it initializes a class or is marked NoInlining, still
    // returns that array.
    private CliTypeIdentity? ReturnedArrayType(MethodInstanceModel method)
    {
        if (method.Signature.ReturnSignatureType.StackKind != CliValueKind.ManagedReference ||
            !method.Definition.HasManagedBody || method.Definition.IsVirtual && !method.Definition.IsFinal)
        {
            return null;
        }
        var body = _methodBodies.ReadMethodBody(method);
        var instructions = body.Instructions;
        var entries = InstructionEntries.Of(body);
        CliTypeIdentity? returned = null;
        for (var index = 0; index < instructions.Length; index++)
        {
            if (instructions[index].Operation != CilOperation.Return)
            {
                continue;
            }
            // The value comes from the instruction before, unless a branch
            // reaches the return with another.
            var producer = index - 1;
            while (producer >= 0 && instructions[producer].Operation == CilOperation.Nop)
            {
                producer--;
            }
            if (producer < 0 ||
                Enumerable.Range(producer + 1, index - producer).Any(next => entries.Contains(instructions[next].Offset)) ||
                instructions[producer].Operation is not (CilOperation.LoadStaticField or CilOperation.LoadField or CilOperation.NewArray) ||
                StaticArrayType(body, instructions[producer], NoArrayLocals) is not { } type ||
                returned is not null && !returned.Equals(type))
            {
                return null;
            }
            returned = type;
        }
        return returned;
    }

    // The helper's first effect, after pushing arguments and constants, reads
    // the length of the array it was passed, which throws for null as the
    // interface call would have.
    private static bool ReadsTheArrayLengthFirst(CilMethodBody helper)
    {
        var operations = helper.Instructions.Where(static instruction => instruction.Operation != CilOperation.Nop).ToArray();
        var effect = Array.FindIndex(operations, static instruction =>
            instruction.Operation is not (CilOperation.LoadArgument or CilOperation.LoadInt32 or CilOperation.LoadNull));
        return effect > 0 &&
            operations[effect].Operation == CilOperation.LoadArrayLength &&
            operations[effect - 1] is { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: 0 } };
    }

    // Locals of another declared type, such as IEnumerable<T>, that every store
    // gives a value of one SZ array type, and whose address is never taken. A
    // read before any store sees null, which the array helper rejects as the
    // interface call would.
    private Dictionary<int, CliTypeIdentity> ArrayLocals(CilMethodBody body, StackSlotProducers producers)
    {
        var stored = new Dictionary<int, CliTypeIdentity?>();
        var exposed = new HashSet<int>();
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            switch (body.Instructions[index])
            {
                case { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var local } }:
                    var type = producers.FindTop(index) is { } producer
                        ? StaticArrayType(body, body.Instructions[producer], NoArrayLocals)
                        : null;
                    stored[local] = !stored.TryGetValue(local, out var previous) || Equals(previous, type) ? type : null;
                    break;
                case { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var address } }:
                    exposed.Add(address);
                    break;
            }
        }
        return stored
            .Where(pair => pair.Value is not null && !exposed.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value!);
    }

    // Locals that every store gives an allocation of one class, never exposed
    // by address except as the receiver of a constrained call. Roslyn reuses
    // one local for the enumerators of successive foreach loops. A load that a
    // store dominates holds such an allocation.
    private Dictionary<int, (List<int> Stores, CliTypeIdentity Type)> ExactLocals(
        CilMethodBody body,
        StackSlotProducers producers)
    {
        var stores = new Dictionary<int, List<int>>();
        var exposed = new HashSet<int>();
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            switch (body.Instructions[index])
            {
                case { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index stored }:
                    if (!stores.TryGetValue(stored.Value, out var list)) stores[stored.Value] = list = [];
                    list.Add(index);
                    break;
                case { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index address }
                    when !IsConstrainedRead(body.Instructions, producers, index):
                    exposed.Add(address.Value);
                    break;
            }
        }
        var exact = new Dictionary<int, (List<int>, CliTypeIdentity)>();
        foreach (var (local, list) in stores)
        {
            if (!exposed.Contains(local) &&
                Allocation(body, producers, list[0]) is { } type &&
                list.Skip(1).All(store => type.Equals(Allocation(body, producers, store))))
            {
                exact[local] = (list, type);
            }
        }
        return exact;
    }

    // A local's address that only serves as the receiver of a constrained call
    // on a reference type, which dereferences it; the call takes no arguments,
    // so the address is the receiver and not a by-reference argument.
    private bool IsConstrainedRead(ImmutableArray<CilInstruction> instructions, StackSlotProducers producers, int address) =>
        address + 2 < instructions.Length &&
        instructions[address].Operation == CilOperation.LoadLocalAddress &&
        instructions[address + 1] is { Operation: CilOperation.Constrained, Operand: CilOperand.TypeIdentity { Value.IsValueType: false } } &&
        instructions[address + 2].Operation == CilOperation.CallVirtual &&
        _calledMethods.Resolve(instructions[address + 2]) is { Definition.IsStatic: false, Signature.ParameterTypes.IsEmpty: true } &&
        !producers.Enters(address + 1) && !producers.Enters(address + 2);

    // The class a stored value was allocated as, following copies made by dup.
    private static CliTypeIdentity? Allocation(
        CilMethodBody body,
        StackSlotProducers producers,
        int consumer)
    {
        while (producers.FindTop(consumer) is { } producer)
        {
            switch (body.Instructions[producer])
            {
                case { Operation: CilOperation.Duplicate }:
                    consumer = producer;
                    continue;
                case { Operation: CilOperation.NewObject, Operand: CilOperand.MethodInstance { Value.DeclaringType: var type } }
                    when !type.IsValueType && type.Shape is CliTypeShape.Named or CliTypeShape.GenericInstantiation:
                    return type;
                default:
                    return null;
            }
        }
        return null;
    }

    // The store runs before the load on every path that reaches the load. The
    // dominance tree follows normal flow only; a handler is entered only from
    // its protected region, which is entered at its start, so a load in a
    // handler is dominated by a store outside the region that dominates the
    // region's start, as foreach's finally is by the enumerator's store.
    private static bool Dominates(
        CilMethodBody body,
        Dictionary<int, int> blocks,
        Dictionary<int, ImmutableHashSet<int>> dominators,
        int store,
        int load)
    {
        var stored = body.Instructions[store].Offset;
        var storeBlock = blocks[stored];
        var loaded = body.Instructions[load].Offset;
        while (true)
        {
            var loadBlock = blocks[loaded];
            if (storeBlock == loadBlock
                    ? stored < loaded
                    : dominators.TryGetValue(loadBlock, out var loadDominators) && loadDominators.Contains(storeBlock))
            {
                return true;
            }
            if (body.ExceptionRegions
                    .Where(region => (region.FilterOffset ?? region.HandlerOffset) <= loaded &&
                        loaded < region.HandlerOffset + region.HandlerLength)
                    .MinBy(region => region.HandlerOffset + region.HandlerLength - (region.FilterOffset ?? region.HandlerOffset)) is not
                    { } handler ||
                handler.TryOffset <= stored && stored < handler.TryOffset + handler.TryLength)
            {
                return false;
            }
            loaded = handler.TryOffset;
        }
    }
}
