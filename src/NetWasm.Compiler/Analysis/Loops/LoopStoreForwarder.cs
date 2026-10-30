using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Analysis.AddressTakenValues;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops;

internal interface ILoopStoreForwarder
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// A loop that updates one element in place, as `sums[i] += x` in an inner loop
// or `span[0] ^= span[k]` does, reads it back from memory on every iteration,
// so each iteration's load waits for the previous one's store. When nothing
// else in the loop writes memory, the element holds what the loop last stored
// there, or what the block before the loop last stored or loaded, so a local
// keeps that value and the loop's loads of the element read the local, each
// extending it as its own type does. Every store stays, so the element is
// current wherever the loop leaves or throws, and other loads that may alias
// it still read the right value. An element is an array's, through ldelem,
// stelem or ldelema, or a reference's, through Unsafe.Add as a span's indexer
// inlines it, at an index that is a constant, a local holding one constant,
// or a local or argument. The array or reference is a local or argument, or a
// field read through the address of a struct local or argument that is only
// ever read so, like a span parameter's. The loop writes none of them, so the
// element the block before the loop accessed is the one every access in the
// loop reaches. LLVM's LICM keeps such an element in a register; .NET's JIT
// reads it from memory.
internal sealed class LoopStoreForwarder(
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IControlFlowNaturalLoopAnalyzer loops,
    IRuntimeIntrinsicRegistry intrinsics,
    bool enabled) : ILoopStoreForwarder
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly IControlFlowNaturalLoopAnalyzer _loops = loops ?? throw new ArgumentNullException(nameof(loops));
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ?? throw new ArgumentNullException(nameof(intrinsics));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled || !body.ExceptionRegions.IsEmpty ||
            !body.Instructions.Any(static instruction =>
                instruction.Operation is CilOperation.StoreArrayElement or CilOperation.StoreObject))
        {
            return body;
        }

        var graph = _graphs.Build(body);
        var entryStacks = _stacks.Validate(graph).InstructionEntryStacks;
        var facts = new MethodFacts(body, entryStacks, new StackSlotProducers(body, entryStacks), _intrinsics);
        var indices = Enumerable.Range(0, body.Instructions.Length).ToDictionary(index => body.Instructions[index].Offset);
        var forwards = new List<Forward>();
        foreach (var loop in _loops.Analyze(graph, graph.Entry.Index, graph.ReachableBlocks))
        {
            var entries = loop.SelectMany(block => graph.Predecessors[block].Where(predecessor => !loop.Contains(predecessor)))
                .Distinct()
                .ToArray();
            if (entries.Length != 1) continue;
            var members = loop.SelectMany(block => graph.GetBlock(block).Instructions)
                .Select(instruction => indices[instruction.Offset])
                .Order()
                .ToArray();
            if (facts.Accesses(members) is not { } accesses) continue;
            var stored = accesses.Where(access => access.Store).Select(access => access.Location).Distinct().ToArray();
            if (stored.Length != 1) continue;
            var location = stored[0];
            if (members.Any(index => facts.Writes(index, location.Base) || facts.Writes(index, location.Index)) ||
                accesses.Any(access => access.Location == location && access.Store && !facts.StoresWhole(access)) ||
                forwards.Any(forward => forward.Accesses.Any(access => members.Contains(access.Instruction))))
            {
                continue;
            }
            var preheader = graph.GetBlock(entries[0]).Instructions.Select(instruction => indices[instruction.Offset]).ToArray();
            if (facts.EntryAccess(preheader, location) is not { } entry) continue;
            forwards.Add(new(location, entry, [.. accesses.Where(access => access.Location == location)]));
        }
        return forwards.Count == 0 ? body : Splice(body, forwards);
    }

    private static CilMethodBody Splice(CilMethodBody body, List<Forward> forwards)
    {
        var locals = body.Locals.ToBuilder();
        var localTypes = body.LocalSignatureTypes.ToBuilder();
        var before = new Dictionary<int, List<CilInstruction>>();
        var after = new Dictionary<int, List<CilInstruction>>();
        var replaced = new Dictionary<int, ImmutableArray<CilInstruction>>();
        foreach (var forward in forwards)
        {
            var type = CliTypeIdentity.FromStackKind(forward.Location.Storage.StackKind);
            locals.Add(type.StackKind);
            localTypes.Add(type);
            var cache = localTypes.Count - 1;
            ImmutableArray<CilInstruction> keep = [Synthetic(CilOperation.Duplicate, new CilOperand.None()), Local(CilOperation.StoreLocal, cache)];
            // The value a store writes, or the value a load in the block before
            // the loop read, becomes the cached value.
            if (forward.Entry.Store) Add(before, forward.Entry.Instruction, keep);
            else Add(after, forward.Entry.Instruction, keep);
            foreach (var access in forward.Accesses)
            {
                if (access.Store)
                {
                    Add(before, access.Instruction, keep);
                    continue;
                }
                // The array and index, or the element's address, are no longer
                // read; a narrow load extends the cached value as it would have.
                var pops = body.Instructions[access.Instruction].Operation == CilOperation.LoadArrayElement ? 2 : 1;
                replaced[access.Instruction] =
                [
                    .. Enumerable.Repeat(Synthetic(CilOperation.Pop, new CilOperand.None()), pops),
                    Local(CilOperation.LoadLocal, cache),
                    .. Extension(access.Type),
                ];
            }
        }

        var starts = new Dictionary<int, int>();
        var replacements = new List<CilInstruction>[body.Instructions.Length];
        var count = 0;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var instruction = body.Instructions[index];
            var replacement = replacements[index] = [];
            if (before.TryGetValue(index, out var prefix)) replacement.AddRange(prefix);
            if (replaced.TryGetValue(index, out var substitute)) replacement.AddRange(substitute);
            else replacement.Add(instruction);
            if (after.TryGetValue(index, out var suffix)) replacement.AddRange(suffix);
            starts.Add(instruction.Offset, count);
            count += replacement.Count;
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
        };

        static void Add(Dictionary<int, List<CilInstruction>> map, int index, ImmutableArray<CilInstruction> code)
        {
            if (!map.TryGetValue(index, out var list)) map[index] = list = [];
            list.AddRange(code);
        }
    }

    // What a load of the type does to the stored value beyond reading it.
    private static ImmutableArray<CilInstruction> Extension(CliTypeIdentity type) =>
        LocalAddressDereferenceFolder.Width(type) switch
        {
            "u1" => [Conversion(8, true)],
            "i1" => [Conversion(8, false)],
            "u2" => [Conversion(16, true)],
            "i2" => [Conversion(16, false)],
            _ => [],
        };

    private static CilInstruction Conversion(int bits, bool unsigned) =>
        Synthetic(CilOperation.ConvertNumeric, new CilOperand.NumericConversion(bits, unsigned, false, false, false));

    private static CilInstruction Synthetic(CilOperation operation, CilOperand operand) => new(0, 1, operation, operand);

    private static CilInstruction Local(CilOperation operation, int local) => Synthetic(operation, new CilOperand.Index(local));

    // Facts about one body: the element each access reaches and the locals and
    // arguments the accesses rely on.
    private sealed record MethodFacts
    {
        // Intrinsics that compute addresses or sizes and write nothing.
        private static readonly ImmutableHashSet<RuntimeIntrinsic> ReadOnlyIntrinsics =
        [
            RuntimeIntrinsic.UnsafeAdd, RuntimeIntrinsic.UnsafeAs, RuntimeIntrinsic.UnsafeAsRef,
            RuntimeIntrinsic.UnsafeAsRefManaged, RuntimeIntrinsic.UnsafeNullRef, RuntimeIntrinsic.UnsafeIsAddressGreaterThan,
            RuntimeIntrinsic.UnsafeSizeOf, RuntimeIntrinsic.UnsafeByteOffset, RuntimeIntrinsic.UnsafeAddByteOffset,
        ];

        private readonly ImmutableArray<CilInstruction> _instructions;
        private readonly ImmutableDictionary<int, ImmutableArray<CliValueKind>> _stacks;
        private readonly StackSlotProducers _producers;
        private readonly Dictionary<EntityKey, RuntimeIntrinsic> _intrinsics = [];
        private readonly HashSet<Value> _exposed = [];
        private readonly Dictionary<int, int> _constants = [];

        public MethodFacts(
            CilMethodBody body,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>> stacks,
            StackSlotProducers producers,
            IRuntimeIntrinsicRegistry intrinsics)
        {
            _instructions = body.Instructions;
            _stacks = stacks;
            _producers = producers;
            foreach (var instruction in _instructions)
            {
                if (instruction.Operation == CilOperation.Call && CalledMethod(instruction) is { } method &&
                    intrinsics.TryGetIntrinsic(method, out var intrinsic))
                {
                    _intrinsics[method] = intrinsic;
                }
            }
            // An address of a local or argument that anything but an ldfld
            // takes may write it or let other code read it.
            var fieldReads = new HashSet<int>();
            for (var index = 0; index < _instructions.Length; index++)
            {
                if (_instructions[index].Operation == CilOperation.LoadField && _stacks.ContainsKey(_instructions[index].Offset) &&
                    _producers.FindTop(index) is { } address)
                {
                    fieldReads.Add(address);
                }
            }
            var stores = new Dictionary<int, HashSet<int?>>();
            for (var index = 0; index < _instructions.Length; index++)
            {
                switch (_instructions[index])
                {
                    case { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var local } }
                        when !fieldReads.Contains(index):
                        _exposed.Add(new(ValueKind.Local, local));
                        break;
                    case { Operation: CilOperation.LoadArgumentAddress, Operand: CilOperand.Index { Value: var argument } }
                        when !fieldReads.Contains(index):
                        _exposed.Add(new(ValueKind.Argument, argument));
                        break;
                    case { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var local } }:
                        if (!stores.TryGetValue(local, out var values)) stores[local] = values = [];
                        values.Add(_stacks.ContainsKey(_instructions[index].Offset) && _producers.FindTop(index) is { } value &&
                            _instructions[value] is { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: var constant } }
                            ? constant : null);
                        break;
                }
            }
            foreach (var (local, values) in stores)
            {
                if (values.Count == 1 && values.Single() is { } constant && !_exposed.Contains(new(ValueKind.Local, local)))
                {
                    _constants[local] = constant;
                }
            }
        }

        // The element accesses of a loop that writes memory only through them,
        // or null when the loop may write memory any other way.
        public List<Access>? Accesses(int[] members)
        {
            var accesses = new List<Access>();
            foreach (var index in members)
            {
                var instruction = _instructions[index];
                if (MayWriteMemory(instruction)) return null;
                if (!_stacks.ContainsKey(instruction.Offset)) continue;
                var access = AccessAt(index);
                if (access is null && instruction.Operation is CilOperation.StoreArrayElement or CilOperation.StoreObject)
                {
                    return null;
                }
                if (access is not null) accesses.Add(access);
            }
            return accesses;
        }

        // The last access to the element in the block before the loop, with
        // nothing after it that writes memory or the element's base or index.
        public Access? EntryAccess(int[] preheader, Location location)
        {
            for (var position = preheader.Length - 1; position >= 0; position--)
            {
                var index = preheader[position];
                if (Writes(index, location.Base) || Writes(index, location.Index) ||
                    !_stacks.ContainsKey(_instructions[index].Offset))
                {
                    return null;
                }
                if (AccessAt(index) is { } access && access.Location == location && (!access.Store || StoresWhole(access)))
                {
                    return access;
                }
                if (MayWriteMemory(_instructions[index]) ||
                    _instructions[index].Operation is CilOperation.StoreArrayElement or CilOperation.StoreObject)
                {
                    return null;
                }
            }
            return null;
        }

        // Whether a store's value is what a local of the element's storage
        // holds: any int for a narrow element, which its loads extend, or a
        // value of the element's own stack kind.
        public bool StoresWhole(Access access) =>
            _stacks[_instructions[access.Instruction].Offset][^1] == access.Location.Storage.StackKind;

        public bool Writes(int index, Value value) => (value.Kind, _instructions[index]) switch
        {
            (ValueKind.Local or ValueKind.LocalField,
                { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var local } }) => local == value.Number,
            (ValueKind.Argument or ValueKind.ArgumentField,
                { Operation: CilOperation.StoreArgument, Operand: CilOperand.Index { Value: var argument } }) => argument == value.Number,
            _ => false,
        };

        private Access? AccessAt(int index)
        {
            var instruction = _instructions[index];
            if (instruction.Operand is not CilOperand.TypeIdentity { Value: var type }) return null;
            var stack = _stacks[instruction.Offset];
            var location = instruction.Operation switch
            {
                CilOperation.StoreArrayElement => ArrayElement(index, stack.Length - 3, type),
                CilOperation.LoadArrayElement => ArrayElement(index, stack.Length - 2, type),
                CilOperation.StoreObject => Addressed(index, stack.Length - 2, type),
                CilOperation.LoadObject => Addressed(index, stack.Length - 1, type),
                _ => null,
            };
            return location is null ? null
                : new(index, location, instruction.Operation is CilOperation.StoreArrayElement or CilOperation.StoreObject, type);
        }

        private Location? ArrayElement(int consumer, int array, CliTypeIdentity type) =>
            Storage.Of(type) is { } storage &&
            Base(_producers.Find(consumer, array)) is { Kind: ValueKind.Local or ValueKind.Argument } arrayValue &&
            Index(_producers.Find(consumer, array + 1)) is { } indexValue
                ? new(arrayValue, indexValue, Scale(indexValue, storage), storage)
                : null;

        // The element an ldobj or stobj reaches: through ldelema, through
        // Unsafe.Add of a reference and an int index, or the reference itself,
        // directly or through the dup a compound assignment makes, also past
        // a bounds check between them that branches around its throw.
        private Location? Addressed(int consumer, int slot, CliTypeIdentity type)
        {
            if (Storage.Of(type) is not { } storage ||
                _producers.Find(consumer, slot, duplicates: true, acrossBranches: true) is not { } producer)
            {
                return null;
            }
            if (_instructions[producer].Operation == CilOperation.Duplicate)
            {
                if (_producers.FindTop(producer, acrossBranches: true) is not { } duplicated) return null;
                producer = duplicated;
            }
            var instruction = _instructions[producer];
            if (!_stacks.TryGetValue(instruction.Offset, out var stack)) return null;
            switch (instruction)
            {
                case { Operation: CilOperation.LoadArrayElementAddress, Operand: CilOperand.TypeIdentity { Value: var element } }:
                    return Storage.Of(element) == storage ? ArrayElement(producer, stack.Length - 2, element) : null;
                case { Operation: CilOperation.Call, Operand: CilOperand.MethodInstance { Value: var method } }
                    when Intrinsic(instruction) == RuntimeIntrinsic.UnsafeAdd && method.MethodArguments.Length == 1 &&
                        Storage.Of(method.MethodArguments[0]) == storage && stack.Length >= 2 &&
                        stack[^2] == CliValueKind.ManagedAddress && stack[^1] is CliValueKind.I4 or CliValueKind.NativeInt:
                    {
                        // A native index is an int that conv.u widens, as upstream's span indexer
                        // passes (nint)(uint)index; its scale names the widening, so such an
                        // access is never taken for one with a sign-extended index.
                        var indexProducer = _producers.Find(producer, stack.Length - 1);
                        var widened = stack[^1] == CliValueKind.NativeInt;
                        if (widened)
                        {
                            indexProducer = indexProducer is { } conversion &&
                                _instructions[conversion].Operation == CilOperation.ConvertNativeUInt &&
                                _stacks.TryGetValue(_instructions[conversion].Offset, out var operands) &&
                                operands[^1] == CliValueKind.I4
                                    ? _producers.FindTop(conversion)
                                    : null;
                        }
                        return Base(_producers.Find(producer, stack.Length - 2)) is { } reference &&
                            Index(indexProducer) is { } offset
                            ? new(reference, offset, Scale(offset, storage) is var scale && widened && scale.Length != 0
                                ? scale + ".u"
                                : scale, storage)
                            : null;
                    }
                default:
                    return Base(producer) is { } direct ? new(direct, new(ValueKind.Constant, 0), "", storage) : null;
            }
        }

        // An index of zero reaches the base whatever the element's size.
        private static string Scale(Value index, Storage storage) =>
            index is { Kind: ValueKind.Constant, Number: 0 } ? "" : storage.Name;

        // A local or argument nothing else addresses, or a field read through
        // the address of a struct local or argument that only ever reads fields.
        private Value? Base(int? producer) => producer is not { } index ? null : _instructions[index] switch
        {
            { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } }
                when !_exposed.Contains(new(ValueKind.Local, local)) => new(ValueKind.Local, local),
            { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: var argument } }
                when !_exposed.Contains(new(ValueKind.Argument, argument)) => new(ValueKind.Argument, argument),
            { Operation: CilOperation.LoadField, Operand: var field } when FieldKey(field) is { } key &&
                _producers.FindTop(index) is { } address => _instructions[address] switch
                {
                    { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var local } }
                        when !_exposed.Contains(new(ValueKind.Local, local)) => new(ValueKind.LocalField, local, key),
                    { Operation: CilOperation.LoadArgumentAddress, Operand: CilOperand.Index { Value: var argument } }
                        when !_exposed.Contains(new(ValueKind.Argument, argument)) => new(ValueKind.ArgumentField, argument, key),
                    _ => null,
                },
            _ => null,
        };

        private Value? Index(int? producer) => producer is not { } index ? null : _instructions[index] switch
        {
            { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: var value } } => new(ValueKind.Constant, value),
            { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } }
                when _constants.TryGetValue(local, out var constant) => new(ValueKind.Constant, constant),
            { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } }
                when !_exposed.Contains(new(ValueKind.Local, local)) => new(ValueKind.Local, local),
            { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: var argument } }
                when !_exposed.Contains(new(ValueKind.Argument, argument)) => new(ValueKind.Argument, argument),
            _ => null,
        };

        private static EntityKey? FieldKey(CilOperand operand) => operand switch
        {
            CilOperand.FieldInstance instance => instance.Value.Definition.Key,
            CilOperand.Entity entity => entity.Key,
            _ => null,
        };

        // Instructions that may write memory other than through an element
        // access this pass identifies, or that may run code which does.
        private bool MayWriteMemory(CilInstruction instruction) => instruction.Operation switch
        {
            CilOperation.Call => Intrinsic(instruction) is not { } intrinsic || !ReadOnlyIntrinsics.Contains(intrinsic),
            _ => instruction.Operation is
                CilOperation.StoreArrayElementReference or CilOperation.StoreRectangularArrayElement or CilOperation.StoreField or
                CilOperation.StoreStaticField or CilOperation.CopyObject or CilOperation.InitializeObject or CilOperation.CopyBlock or
                CilOperation.InitializeBlock or CilOperation.CallVirtual or CilOperation.CallIndirect or CilOperation.NewObject or
                CilOperation.Volatile or CilOperation.CompareExchange or CilOperation.LocalAllocate,
        };

        private RuntimeIntrinsic? Intrinsic(CilInstruction instruction) =>
            CalledMethod(instruction) is { } method && _intrinsics.TryGetValue(method, out var intrinsic) ? intrinsic : null;

        private static EntityKey? CalledMethod(CilInstruction instruction) => instruction.Operand switch
        {
            CilOperand.Entity entity => entity.Key,
            CilOperand.MethodInstance instance => instance.Value.Definition.Key,
            _ => null,
        };
    }

    // How an element is stored: narrow integers of one width share storage
    // whatever their sign, which each load applies.
    private sealed record Storage(string Name, CliValueKind StackKind)
    {
        public static Storage? Of(CliTypeIdentity type) =>
            type.Shape != CliTypeShape.Primitive ? null : LocalAddressDereferenceFolder.Width(type) switch
            {
                "u1" or "i1" => new("8", CliValueKind.I4),
                "u2" or "i2" => new("16", CliValueKind.I4),
                "i4" => new("32", CliValueKind.I4),
                "i8" => new("64", CliValueKind.I8),
                "f4" => new("f4", CliValueKind.F4),
                "f8" => new("f8", CliValueKind.F8),
                "native" => new("native", CliValueKind.NativeInt),
                _ => null,
            };
    }

    private enum ValueKind
    {
        Local,
        Argument,
        Constant,
        LocalField,
        ArgumentField,
    }

    private sealed record Value(ValueKind Kind, int Number, EntityKey? Field = null);

    private sealed record Location(Value Base, Value Index, string Scale, Storage Storage);

    private sealed record Access(int Instruction, Location Location, bool Store, CliTypeIdentity Type);

    private sealed record Forward(Location Location, Access Entry, ImmutableArray<Access> Accesses);
}
