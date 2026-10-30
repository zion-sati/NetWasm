using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values;

internal sealed class ManagedAggregateValueAnalyzer(
    IFieldRepository fieldRepository,
    IMethodRepository methodRepository,
    ITypeDefinitionResolver typeDefinitions,
    IValueLayoutProvider values,
    IInstanceFieldLayoutProvider fields,
    IArgumentSignatureTypeResolver argumentTypes,
    ICilTypeOperandResolver typeOperands,
    ITargetLayout layouts,
    IRuntimeIntrinsicRegistry intrinsics) : IManagedAggregateValueAnalyzer
{
    private readonly IFieldRepository _fieldRepository = fieldRepository ??
        throw new ArgumentNullException(nameof(fieldRepository));
    private readonly IMethodRepository _methodRepository = methodRepository ??
        throw new ArgumentNullException(nameof(methodRepository));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly IValueLayoutProvider _values = values ??
        throw new ArgumentNullException(nameof(values));
    private readonly IInstanceFieldLayoutProvider _fields = fields ??
        throw new ArgumentNullException(nameof(fields));
    private readonly IArgumentSignatureTypeResolver _argumentTypes = argumentTypes ??
        throw new ArgumentNullException(nameof(argumentTypes));
    private readonly ICilTypeOperandResolver _typeOperands = typeOperands ??
        throw new ArgumentNullException(nameof(typeOperands));
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ??
        throw new ArgumentNullException(nameof(intrinsics));

    public ManagedAggregateValueAnalysis Analyze(
        ManagedAggregateValueAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Method);
        ArgumentNullException.ThrowIfNull(request.RootMap);
        ArgumentNullException.ThrowIfNull(request.ByRefEffects);
        if (request.RootMap.Method != request.Method.Header.Method.Key)
        {
            throw new InvalidOperationException(
                "Aggregate value analysis received a root map for another method.");
        }
        if (!request.Enabled ||
            !request.Method.ExceptionGroups.IsEmpty ||
            !request.RootMap.AnchorPlan.Anchors.IsEmpty)
        {
            return ManagedAggregateValueAnalysis.Empty;
        }

        var storages = FindStorages(request);
        if (storages.Count == 0)
        {
            return ManagedAggregateValueAnalysis.Empty;
        }

        var rejected = new HashSet<ManagedAggregateStorageId>();
        var accesses = new Dictionary<int, ManagedAggregateLaneAccess>();
        var rootDependencies = new List<ManagedAggregateRootDependency>();
        var materializations = new List<ManagedAggregateMaterialization>();
        var wholeValueStores = new List<(StructuredBlockId Block, ManagedAggregateStorageId Storage)>();
        var entries = new Dictionary<StructuredBlockId, FlowState>();
        var pending = new Queue<StructuredBlockId>();
        Merge(request.Method.EntryBlock, new(
            [],
            InitialLocals(request.Method.Header.LocalSignatureTypes)));

        while (pending.TryDequeue(out var blockId))
        {
            var block = request.Method.Blocks[blockId];
            var stack = entries[blockId].Stack.ToList();
            var locals = entries[blockId].Locals.ToBuilder();
            for (var index = 0; index < block.Instructions.Length; index++)
            {
                var instruction = block.Instructions[index];
                RecordRootObligations(
                    request.RootMap,
                    instruction.Offset,
                    stack,
                    locals,
                    rootDependencies,
                    rejected);
                var produced = ProducedCount(instruction);
                var outgoingKinds = OutgoingKinds(request.Method, block, index);
                var consumed = stack.Count + produced - outgoingKinds.Length;
                if (consumed < 0 || consumed > stack.Count)
                {
                    throw new InvalidOperationException(
                        $"Aggregate value analysis disagrees with the validated CIL stack " +
                        $"at IL_{instruction.Offset:x4} ({instruction.Operation}): " +
                        $"entry={stack.Count}, produced={produced}, " +
                        $"outgoing={outgoingKinds.Length}.");
                }

                var producedOrigins = AddressOrigins.None;
                switch (instruction.Operation)
                {
                    case CilOperation.LoadArgumentAddress:
                        producedOrigins = RootOrigins(
                            storages,
                            ManagedAggregateStorageKind.Argument,
                            CilOperandReader.GetIndex(instruction));
                        break;
                    case CilOperation.LoadLocalAddress:
                        producedOrigins = RootOrigins(
                            storages,
                            ManagedAggregateStorageKind.Local,
                            CilOperandReader.GetIndex(instruction));
                        break;
                    case CilOperation.LoadArgument:
                        {
                            var argument = CilOperandReader.GetIndex(instruction);
                            var type = _argumentTypes.Resolve(request.Method.Header, argument);
                            if (type.StackKind == CliValueKind.ManagedAddress)
                            {
                                producedOrigins = AddressOrigins.UnknownOnly;
                            }
                            else
                            {
                                RecordWholeValueMaterialization(
                                    instruction.Offset,
                                    ManagedAggregateStorageKind.Argument,
                                    argument,
                                    storages,
                                    materializations);
                            }
                            break;
                        }
                    case CilOperation.LoadLocal:
                        {
                            var local = CilOperandReader.GetIndex(instruction);
                            if (request.Method.Header.LocalSignatureTypes[local].StackKind ==
                                CliValueKind.ManagedAddress)
                            {
                                producedOrigins = locals[local];
                            }
                            else
                            {
                                RecordWholeValueMaterialization(
                                    instruction.Offset,
                                    ManagedAggregateStorageKind.Local,
                                    local,
                                    storages,
                                    materializations);
                            }
                            break;
                        }
                    case CilOperation.StoreLocal:
                        {
                            var local = CilOperandReader.GetIndex(instruction);
                            if (request.Method.Header.LocalSignatureTypes[local].StackKind ==
                                CliValueKind.ManagedAddress)
                            {
                                locals[local] = stack[^1];
                            }
                            RejectStoredAddress(stack[^1],
                                request.Method.Header.LocalSignatureTypes[local].StackKind !=
                                    CliValueKind.ManagedAddress,
                                rejected);
                            RecordWholeValueStore(
                                instruction.Offset,
                                blockId,
                                ManagedAggregateStorageKind.Local,
                                local,
                                storages,
                                materializations,
                                wholeValueStores);
                            break;
                        }
                    case CilOperation.Duplicate:
                        stack.Add(stack[^1]);
                        continue;
                    case CilOperation.LoadFieldAddress:
                        producedOrigins = ResolveFieldAddress(
                            instruction,
                            stack[^1],
                            storages,
                            rejected);
                        break;
                    case CilOperation.LoadField:
                        RecordFieldAccess(
                            instruction,
                            blockId,
                            stack[^1],
                            writes: false,
                            storages,
                            accesses,
                            rejected);
                        break;
                    case CilOperation.StoreField:
                        RecordFieldAccess(
                            instruction,
                            blockId,
                            stack[^2],
                            writes: true,
                            storages,
                            accesses,
                            rejected);
                        Reject(stack[^1], rejected);
                        break;
                    case CilOperation.LoadObject:
                        RecordObjectAccess(
                            instruction,
                            blockId,
                            stack[^1],
                            writes: false,
                            storages,
                            accesses,
                            rejected,
                            request.Method.Header.MethodInstance);
                        break;
                    case CilOperation.StoreObject:
                        RecordObjectAccess(
                            instruction,
                            blockId,
                            stack[^2],
                            writes: true,
                            storages,
                            accesses,
                            rejected,
                            request.Method.Header.MethodInstance);
                        Reject(stack[^1], rejected);
                        break;
                    case CilOperation.InitializeObject when request.References:
                        RecordClear(
                            instruction,
                            blockId,
                            stack[^1],
                            storages,
                            accesses,
                            rejected,
                            request.Method.Header.MethodInstance);
                        break;
                    case CilOperation.Return:
                    case CilOperation.StoreStaticField:
                    case CilOperation.StoreArrayElement:
                    case CilOperation.StoreArrayElementReference:
                    case CilOperation.StoreRectangularArrayElement:
                    case CilOperation.CallIndirect:
                    case CilOperation.CopyObject:
                    case CilOperation.InitializeObject:
                    case CilOperation.CopyBlock:
                    case CilOperation.InitializeBlock:
                    case CilOperation.CompareExchange:
                        RejectConsumed(stack, consumed, rejected);
                        break;
                    case CilOperation.StoreArgument:
                        RejectConsumed(stack, consumed, rejected);
                        RecordWholeValueStore(
                            instruction.Offset,
                            blockId,
                            ManagedAggregateStorageKind.Argument,
                            CilOperandReader.GetIndex(instruction),
                            storages,
                            materializations,
                            wholeValueStores);
                        break;
                    case CilOperation.Call:
                        producedOrigins = ApplyCall(
                            request.ByRefEffects,
                            instruction,
                            stack,
                            consumed,
                            storages,
                            materializations,
                            rejected);
                        break;
                    case CilOperation.CallVirtual:
                    case CilOperation.NewObject:
                        RejectConsumed(stack, consumed, rejected);
                        break;
                    default:
                        if (ProducesManagedAddress(outgoingKinds, produced))
                        {
                            RejectConsumed(stack, consumed, rejected);
                            producedOrigins = AddressOrigins.UnknownOnly;
                        }
                        else if (!IsAddressTransparent(instruction.Operation))
                        {
                            RejectConsumed(stack, consumed, rejected);
                        }
                        break;
                }

                if (ProducesManagedAddress(outgoingKinds, produced) &&
                    !producedOrigins.Unknown &&
                    producedOrigins.Recipes.IsEmpty)
                {
                    producedOrigins = AddressOrigins.UnknownOnly;
                }

                stack.RemoveRange(stack.Count - consumed, consumed);
                if (produced != 0)
                {
                    stack.Add(producedOrigins);
                }
            }

            ApplyExit(
                request.Method,
                request.RootMap,
                block,
                stack,
                locals,
                rootDependencies,
                rejected);

            foreach (var successor in Successors(block.Exit))
            {
                if (request.Method.Blocks.ContainsKey(successor))
                {
                    Merge(successor, new([.. stack], locals.ToImmutable()));
                }
            }
        }

        RejectUnamortizedStores(request.Method, wholeValueStores, accesses, rejected);
        var selected = storages
            .Where(pair => !rejected.Contains(pair.Key))
            .ToImmutableDictionary();
        if (selected.IsEmpty)
        {
            return ManagedAggregateValueAnalysis.Empty;
        }

        var selectedAccesses = accesses
            .Where(pair => selected.ContainsKey(pair.Value.Address.Storage))
            .ToImmutableDictionary();
        var liveIn = entries.ToImmutableDictionary(
            pair => pair.Key,
            pair => Storages(pair.Value)
                .Where(selected.ContainsKey)
                .ToImmutableHashSet());
        return new(
            selected,
            selectedAccesses,
            liveIn,
            [.. rootDependencies.Where(dependency =>
                    selected.ContainsKey(dependency.Address.Storage))
                .Distinct()],
            [.. materializations
                .Where(materialization =>
                    selected.ContainsKey(materialization.Storage))
                .GroupBy(materialization =>
                    (materialization.InstructionOffset, materialization.Storage))
                .Select(group => new ManagedAggregateMaterialization(
                    group.Key.InstructionOffset,
                    group.Key.Storage,
                    group.Any(materialization => materialization.ReloadAfter)))]);

        void Merge(StructuredBlockId block, FlowState incoming)
        {
            if (!entries.TryGetValue(block, out var existing))
            {
                entries.Add(block, incoming);
                pending.Enqueue(block);
                return;
            }

            var merged = new FlowState(
                Union(existing.Stack, incoming.Stack),
                Union(existing.Locals, incoming.Locals));
            if (merged.Stack.AsSpan().SequenceEqual(existing.Stack.AsSpan()) &&
                merged.Locals.AsSpan().SequenceEqual(existing.Locals.AsSpan()))
            {
                return;
            }
            entries[block] = merged;
            pending.Enqueue(block);
        }
    }

    private AddressOrigins ApplyCall(
        ManagedByRefEffects effects,
        CilInstruction instruction,
        IReadOnlyList<AddressOrigins> stack,
        int consumed,
        Dictionary<ManagedAggregateStorageId, ManagedAggregateStorage> storages,
        List<ManagedAggregateMaterialization> materializations,
        HashSet<ManagedAggregateStorageId> rejected)
    {
        if (instruction.Operand is not CilOperand.MethodInstance operand)
        {
            RejectConsumed(stack, consumed, rejected);
            return AddressOrigins.None;
        }
        if (_intrinsics.TryGetIntrinsic(
                operand.Value.Definition.Key,
                out var intrinsic))
        {
            if (IsAliasPreservingIntrinsic(intrinsic))
            {
                var expectedConsumed = operand.Value.Signature.ParameterTypes.Length +
                    (operand.Value.Definition.IsStatic ? 0 : 1);
                if (consumed != 1 || consumed != expectedConsumed)
                {
                    RejectConsumed(stack, consumed, rejected);
                    return AddressOrigins.UnknownOnly;
                }
                var firstAlias = stack.Count - consumed;
                var origins = stack[firstAlias];
                var returnType = operand.Value.Signature.ReturnSignatureType;
                if (returnType.Shape != CliTypeShape.ManagedByReference ||
                    returnType.ElementType is not { } elementType ||
                    !TryGetAliasViewSize(elementType, out var viewSize) ||
                    origins.Recipes.Any(recipe =>
                        viewSize > storages[recipe.Storage].Size - recipe.ByteOffset))
                {
                    Reject(origins, rejected);
                    return AddressOrigins.UnknownOnly;
                }
                return new(
                    origins.Unknown,
                    [.. origins.Recipes.Select(recipe =>
                        recipe with { Type = elementType })]);
            }
            if (operand.Value.Signature.ReturnType == CliValueKind.ManagedAddress)
            {
                RejectConsumed(stack, consumed, rejected);
                return AddressOrigins.UnknownOnly;
            }
        }
        if (
            !effects.Methods.TryGetValue(
                operand.Value.CanonicalName,
                out var targetEffects))
        {
            RejectConsumed(stack, consumed, rejected);
            return AddressOrigins.None;
        }

        if (operand.Value.Signature.ReturnType == CliValueKind.ManagedAddress)
        {
            // Ordinary managed-byref call summaries record only may-alias
            // argument indices. They do not prove exact identity, offset, or
            // whether another path can return external storage.
            RejectConsumed(stack, consumed, rejected);
            return AddressOrigins.UnknownOnly;
        }

        var first = stack.Count - consumed;
        for (var index = 0; index < consumed; index++)
        {
            var origins = stack[first + index];
            if (origins.Recipes.IsEmpty)
            {
                continue;
            }
            var argument = index;
            if (targetEffects.EscapedArguments.Contains(argument))
            {
                Reject(origins, rejected);
                continue;
            }
            foreach (var storage in origins.Recipes
                         .Select(recipe => recipe.Storage)
                         .Distinct())
            {
                materializations.Add(new(instruction.Offset, storage,
                    targetEffects.WrittenArguments.Contains(argument)));
            }
        }
        return AddressOrigins.None;
    }

    private bool TryGetAliasViewSize(CliTypeIdentity type, out int size)
    {
        if (type.StackKind == CliValueKind.ValueType)
        {
            var layout = _values.GetValueLayout(type);
            size = layout.Size;
            return size > 0 &&
                layout.ReferenceOffsets.IsEmpty &&
                layout.ByReferenceOffsets.IsEmpty;
        }
        if (type.StackKind is CliValueKind.I4 or CliValueKind.I8 or
            CliValueKind.F4 or CliValueKind.F8 or CliValueKind.NativeInt)
        {
            size = _layouts.Target.GetStorageSize(type);
            return size > 0;
        }
        size = 0;
        return false;
    }

    private static bool IsAliasPreservingIntrinsic(RuntimeIntrinsic intrinsic) =>
        intrinsic is RuntimeIntrinsic.UnsafeAs or RuntimeIntrinsic.UnsafeAsRef or
            RuntimeIntrinsic.UnsafeAsRefManaged;

    private static void RecordWholeValueMaterialization(
        int instructionOffset,
        ManagedAggregateStorageKind kind,
        int index,
        Dictionary<ManagedAggregateStorageId, ManagedAggregateStorage> storages,
        List<ManagedAggregateMaterialization> materializations)
    {
        var id = new ManagedAggregateStorageId(kind, index);
        if (storages.TryGetValue(id, out var storage))
        {
            materializations.Add(new(instructionOffset, storage.Id));
        }
    }

    // A store of the whole value writes the backing storage, as a call given
    // its address may; the lanes reload from it afterwards.
    private static void RecordWholeValueStore(
        int instructionOffset,
        StructuredBlockId block,
        ManagedAggregateStorageKind kind,
        int index,
        Dictionary<ManagedAggregateStorageId, ManagedAggregateStorage> storages,
        List<ManagedAggregateMaterialization> materializations,
        List<(StructuredBlockId Block, ManagedAggregateStorageId Storage)> wholeValueStores)
    {
        var id = new ManagedAggregateStorageId(kind, index);
        if (storages.ContainsKey(id))
        {
            materializations.Add(new(instructionOffset, id, ReloadAfter: true));
            wholeValueStores.Add((block, id));
        }
    }

    // Each whole-value store flushes and reloads every lane, so lanes pay off
    // only where they are accessed in a loop the stores sit outside of, as an
    // enumerator is assigned before the loop that moves it. A value copied
    // into the storage on every iteration stays in memory.
    private static void RejectUnamortizedStores(
        StructuredMethod method,
        List<(StructuredBlockId Block, ManagedAggregateStorageId Storage)> wholeValueStores,
        Dictionary<int, ManagedAggregateLaneAccess> accesses,
        HashSet<ManagedAggregateStorageId> rejected)
    {
        if (wholeValueStores.Count == 0)
        {
            return;
        }
        var depths = LoopDepths(method.Body);
        foreach (var stores in wholeValueStores.GroupBy(store => store.Storage))
        {
            var deepestAccess = accesses.Values
                .Where(access => access.Address.Storage == stores.Key)
                .Select(access => depths.GetValueOrDefault(access.Block))
                .DefaultIfEmpty(-1)
                .Max();
            if (stores.Any(store => depths.GetValueOrDefault(store.Block) >= deepestAccess))
            {
                Reject(stores.Key, rejected);
            }
        }
    }

    // How many loops enclose each block; a replicated block takes its deepest
    // occurrence.
    private static Dictionary<StructuredBlockId, int> LoopDepths(StructuredSequence body)
    {
        var depths = new Dictionary<StructuredBlockId, int>();
        Visit(body, 0);
        return depths;

        void Add(StructuredBlockOccurrence? occurrence, int depth)
        {
            if (occurrence is not null)
            {
                depths[occurrence.Block] = Math.Max(depths.GetValueOrDefault(occurrence.Block), depth);
            }
        }

        void Visit(StructuredSequence sequence, int depth)
        {
            foreach (var region in sequence.Regions)
            {
                switch (region)
                {
                    case StructuredCode code:
                        Add(code.Occurrence, depth);
                        break;
                    case StructuredSequence nested:
                        Visit(nested, depth);
                        break;
                    case StructuredIf conditional:
                        Add(conditional.Condition, depth);
                        Visit(conditional.WhenTrue, depth);
                        Visit(conditional.WhenFalse, depth);
                        break;
                    case StructuredLoop loop:
                        Add(loop.Condition, depth + 1);
                        Visit(loop.Body, depth + 1);
                        Visit(loop.ContinueBody, depth + 1);
                        Visit(loop.ExitBody, depth);
                        break;
                    case StructuredPostTestLoop loop:
                        Add(loop.Condition, depth + 1);
                        Visit(loop.Body, depth + 1);
                        Visit(loop.ContinueBody, depth + 1);
                        Visit(loop.ExitBody, depth);
                        break;
                    case StructuredLabeledBlock labeled:
                        Visit(labeled.Body, depth);
                        break;
                    case StructuredLabeledLoop labeled:
                        Visit(labeled.Body, depth + 1);
                        break;
                    case StructuredDispatcher dispatcher:
                        foreach (var block in dispatcher.Blocks)
                        {
                            Add(block.Occurrence, depth + 1);
                        }
                        foreach (var exit in dispatcher.Exits)
                        {
                            Visit(exit.Body, depth);
                        }
                        break;
                }
            }
        }
    }

    private static void ApplyExit(
        StructuredMethod method,
        MethodRootMap rootMap,
        StructuredBlockDefinition block,
        List<AddressOrigins> stack,
        ImmutableArray<AddressOrigins>.Builder locals,
        List<ManagedAggregateRootDependency> rootDependencies,
        HashSet<ManagedAggregateStorageId> rejected)
    {
        var exitOffset = block.Exit switch
        {
            StructuredBranchExit branch => branch.InstructionOffset,
            StructuredConditionalExit conditional =>
                conditional.Condition.InstructionOffset,
            StructuredLeaveExit leave => leave.InstructionOffset,
            StructuredTerminalExit terminal => terminal.Instruction.Offset,
            _ => (int?)null,
        };
        if (exitOffset is { } offset)
        {
            RecordRootObligations(
                rootMap,
                offset,
                stack,
                locals,
                rootDependencies,
                rejected);
        }

        var successors = Successors(block.Exit);
        var outgoingCount = successors.IsEmpty
            ? 0
            : method.Blocks[successors[0]].EntryStack.Length;
        var consumed = stack.Count - outgoingCount;
        if (consumed < 0)
        {
            throw new InvalidOperationException(
                "Aggregate value analysis encountered an invalid structured exit stack.");
        }
        RejectConsumed(stack, consumed, rejected);
        stack.RemoveRange(outgoingCount, consumed);
    }

    private static void RecordRootObligations(
        MethodRootMap rootMap,
        int instructionOffset,
        IReadOnlyList<AddressOrigins> stack,
        ImmutableArray<AddressOrigins>.Builder locals,
        List<ManagedAggregateRootDependency> dependencies,
        HashSet<ManagedAggregateStorageId> rejected)
    {
        if (!rootMap.Safepoints.TryGetValue(instructionOffset, out var safepoint))
        {
            return;
        }
        Record(safepoint.Roots, ManagedAggregateRootSetKind.Ordinary);
        Record(safepoint.ConstructorCallRoots, ManagedAggregateRootSetKind.Constructor);

        void Record(
            ImmutableArray<RootSource> sources,
            ManagedAggregateRootSetKind set)
        {
            foreach (var source in sources)
            {
                var origins = source.Kind switch
                {
                    RootSourceKind.ManagedAddressLocal
                        when (uint)source.Index < (uint)locals.Count => locals[source.Index],
                    RootSourceKind.ManagedAddressEvaluationStack
                        when (uint)source.Index < (uint)stack.Count => stack[source.Index],
                    RootSourceKind.EvaluationStack
                        when source.IsIndirect &&
                            (uint)source.Index < (uint)stack.Count => stack[source.Index],
                    _ => AddressOrigins.None,
                };
                if (origins.Recipes.IsEmpty)
                {
                    continue;
                }
                if (source.IsIndirect || origins.Unknown || origins.Recipes.Count != 1)
                {
                    Reject(origins, rejected);
                    continue;
                }
                dependencies.Add(new(
                    instructionOffset,
                    source,
                    origins.Recipes.Single(),
                    set));
            }
        }
    }

    private Dictionary<ManagedAggregateStorageId, ManagedAggregateStorage> FindStorages(
        ManagedAggregateValueAnalysisRequest request)
    {
        var result = new Dictionary<ManagedAggregateStorageId, ManagedAggregateStorage>();
        var methodEffects = request.Method.Header.MethodInstance is { } methodInstance &&
            request.ByRefEffects.Methods.TryGetValue(
                methodInstance.CanonicalName,
                out var resolvedEffects)
                ? resolvedEffects
                : null;
        foreach (var index in request.Method.Header.Instructions
                     .Where(instruction =>
                         instruction.Operation == CilOperation.LoadArgumentAddress)
                     .Select(CilOperandReader.GetIndex)
                     .Distinct())
        {
            if (methodEffects?.RequiresOwnedStorage(index) == true)
            {
                AddStorage(
                    result,
                    new(ManagedAggregateStorageKind.Argument, index),
                    _argumentTypes.Resolve(request.Method.Header, index),
                    owned: true,
                    request.References);
            }
        }

        foreach (var index in request.Method.Header.Instructions
                     .Where(instruction =>
                         instruction.Operation == CilOperation.LoadLocalAddress)
                     .Select(CilOperandReader.GetIndex)
                     .Distinct()
                     .Where(index => !request.ExcludedLocals.Contains(index)))
        {
            AddStorage(
                result,
                new(ManagedAggregateStorageKind.Local, index),
                request.Method.Header.LocalSignatureTypes[index],
                owned: true,
                request.References);
        }
        return result;
    }

    private void AddStorage(
        Dictionary<ManagedAggregateStorageId, ManagedAggregateStorage> storages,
        ManagedAggregateStorageId id,
        CliTypeIdentity type,
        bool owned,
        bool references)
    {
        if (type.StackKind != CliValueKind.ValueType)
        {
            return;
        }
        var layout = _values.GetValueLayout(type);
        if (layout.Size is < 8 or > 16 ||
            layout.Size % sizeof(int) != 0 ||
            LaneShapes(layout, references) is not { } lanes)
        {
            return;
        }
        storages.Add(id, new(id, type, layout.Size, owned) { LaneShapes = lanes });
    }

    // A reference or byref field becomes a lane of its own, which the root
    // planner publishes like a local of that kind; the rest is 4-byte lanes.
    private ImmutableArray<ManagedAggregateLaneShape>? LaneShapes(ValueLayout layout, bool references)
    {
        if (layout.ReferenceOffsets.IsEmpty && layout.ByReferenceOffsets.IsEmpty)
        {
            return [];
        }
        if (!references)
        {
            return null;
        }
        var pointer = _layouts.Target.ObjectReferenceSize;
        var tracked = layout.ReferenceOffsets.Concat(layout.ByReferenceOffsets).ToHashSet();
        var lanes = ImmutableArray.CreateBuilder<ManagedAggregateLaneShape>();
        for (var offset = 0; offset < layout.Size;)
        {
            if (!tracked.Contains(offset))
            {
                lanes.Add(new(offset, sizeof(int), CliValueKind.I4));
                offset += sizeof(int);
                continue;
            }
            if (offset % pointer != 0 || offset + pointer > layout.Size)
            {
                return null;
            }
            lanes.Add(new(offset, pointer, layout.ByReferenceOffsets.Contains(offset)
                ? CliValueKind.ManagedAddress
                : CliValueKind.ManagedReference));
            offset += pointer;
        }
        // A tracked offset inside another lane would be split.
        return tracked.All(offset => lanes.Any(lane => lane.ByteOffset == offset)) ? lanes.ToImmutable() : null;
    }

    // initobj of the whole aggregate writes zero to every lane.
    private void RecordClear(
        CilInstruction instruction,
        StructuredBlockId block,
        AddressOrigins origins,
        Dictionary<ManagedAggregateStorageId, ManagedAggregateStorage> storages,
        Dictionary<int, ManagedAggregateLaneAccess> accesses,
        HashSet<ManagedAggregateStorageId> rejected,
        MethodInstanceModel? methodInstance)
    {
        if (!TrySingleRecipe(origins, rejected, out var recipe))
        {
            return;
        }
        var storage = storages[recipe.Storage];
        var type = _typeOperands.Resolve(instruction, methodInstance);
        if (!recipe.Type.Equals(type))
        {
            Reject(recipe.Storage, rejected);
            return;
        }
        // initobj of the whole value, or of a field such as an enumerator's
        // current element, zeroes the lanes its bytes cover; a field narrower
        // than a lane would need a partial lane write.
        var size = recipe.ByteOffset == 0 && type.Equals(storage.Type)
            ? storage.Size
            : type.StackKind == CliValueKind.ValueType
                ? _values.GetValueLayout(type).Size
                : _layouts.Target.GetStorageSize(type);
        var end = recipe.ByteOffset + size;
        var lanes = storage.Lanes
            .Where(lane => lane.ByteOffset < end && lane.ByteOffset + lane.Size > recipe.ByteOffset)
            .ToArray();
        if (lanes.Length == 0 || lanes[0].ByteOffset != recipe.ByteOffset ||
            lanes[^1].ByteOffset + lanes[^1].Size != end)
        {
            Reject(recipe.Storage, rejected);
            return;
        }
        accesses[instruction.Offset] = new(instruction.Offset, block, recipe, size, CliValueKind.ValueType, Writes: true)
        {
            LaneOffsets = [.. lanes.Select(static lane => lane.ByteOffset)],
        };
    }

    private AddressOrigins ResolveFieldAddress(
        CilInstruction instruction,
        AddressOrigins origins,
        Dictionary<ManagedAggregateStorageId, ManagedAggregateStorage> storages,
        HashSet<ManagedAggregateStorageId> rejected)
    {
        if (origins.Unknown)
        {
            Reject(origins, rejected);
            return AddressOrigins.UnknownOnly;
        }

        var recipes = ImmutableHashSet.CreateBuilder<ManagedAggregateAddressRecipe>();
        foreach (var recipe in origins.Recipes)
        {
            if (!TryResolveField(recipe.Type, instruction.Operand,
                    out var field, out var fieldType))
            {
                Reject(recipe.Storage, rejected);
                continue;
            }
            if (recipe.ByteOffset + field.Offset < 0 ||
                recipe.ByteOffset + field.Offset + field.Size > storages[recipe.Storage].Size)
            {
                Reject(recipe.Storage, rejected);
                continue;
            }
            recipes.Add(recipe with
            {
                ByteOffset = recipe.ByteOffset + field.Offset,
                Type = fieldType,
            });
        }
        return new(false, recipes.ToImmutable());
    }

    private void RecordFieldAccess(
        CilInstruction instruction,
        StructuredBlockId block,
        AddressOrigins origins,
        bool writes,
        Dictionary<ManagedAggregateStorageId, ManagedAggregateStorage> storages,
        Dictionary<int, ManagedAggregateLaneAccess> accesses,
        HashSet<ManagedAggregateStorageId> rejected)
    {
        if (!TrySingleRecipe(origins, rejected, out var recipe))
        {
            return;
        }
        if (!TryResolveField(recipe.Type, instruction.Operand,
                out var field, out var type))
        {
            Reject(recipe.Storage, rejected);
            return;
        }
        RecordAccess(
            instruction,
            block,
            recipe with
            {
                ByteOffset = recipe.ByteOffset + field.Offset,
                Type = type,
            },
            field.Size,
            type.StackKind,
            writes,
            storages,
            accesses,
            rejected);
    }

    private void RecordObjectAccess(
        CilInstruction instruction,
        StructuredBlockId block,
        AddressOrigins origins,
        bool writes,
        Dictionary<ManagedAggregateStorageId, ManagedAggregateStorage> storages,
        Dictionary<int, ManagedAggregateLaneAccess> accesses,
        HashSet<ManagedAggregateStorageId> rejected,
        MethodInstanceModel? methodInstance)
    {
        if (!TrySingleRecipe(origins, rejected, out var recipe))
        {
            return;
        }
        var type = _typeOperands.Resolve(instruction, methodInstance);
        if (type.StackKind is not (
                CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or
                CliValueKind.F8 or CliValueKind.NativeInt) ||
            recipe.Type.StackKind != type.StackKind)
        {
            Reject(recipe.Storage, rejected);
            return;
        }
        RecordAccess(
            instruction,
            block,
            recipe with { Type = type },
            _layouts.Target.GetStorageSize(type),
            type.StackKind,
            writes,
            storages,
            accesses,
            rejected);
    }

    private static void RecordAccess(
        CilInstruction instruction,
        StructuredBlockId block,
        ManagedAggregateAddressRecipe address,
        int size,
        CliValueKind kind,
        bool writes,
        Dictionary<ManagedAggregateStorageId, ManagedAggregateStorage> storages,
        Dictionary<int, ManagedAggregateLaneAccess> accesses,
        HashSet<ManagedAggregateStorageId> rejected)
    {
        var storage = storages[address.Storage];
        if (address.ByteOffset < 0 ||
            address.ByteOffset + size > storage.Size)
        {
            Reject(address.Storage, rejected);
            return;
        }
        // An access that touches a reference or byref lane must be exactly that lane.
        var covered = storage.Lanes
            .Where(lane => lane.ByteOffset < address.ByteOffset + size && address.ByteOffset < lane.ByteOffset + lane.Size)
            .ToArray();
        var tracked = covered.Any(static lane => lane.Kind != CliValueKind.I4);
        if (tracked && (covered.Length != 1 || covered[0] != new ManagedAggregateLaneShape(address.ByteOffset, size, kind)))
        {
            Reject(address.Storage, rejected);
            return;
        }
        accesses[instruction.Offset] = new(
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

    private bool TryResolveField(
        CliTypeIdentity aggregateType,
        CilOperand operand,
        out FieldLayout layout,
        out CliTypeIdentity fieldType)
    {
        switch (operand)
        {
            case CilOperand.FieldInstance instance
                when !instance.Value.Definition.IsStatic &&
                    instance.Value.DeclaringType.Equals(aggregateType):
                layout = _fields.GetFieldLayout(instance.Value);
                fieldType = instance.Value.FieldType;
                return true;
            case CilOperand.Entity entity
                when aggregateType.Shape != CliTypeShape.GenericInstantiation:
                var definition = _fieldRepository.GetField(entity.Key);
                if (!definition.IsStatic &&
                    definition.DeclaringType ==
                        _typeDefinitions.ResolveTypeIdentity(aggregateType).Key)
                {
                    layout = _fields.GetFieldLayout(entity.Key);
                    fieldType = definition.SignatureType;
                    return true;
                }
                break;
        }
        layout = default;
        fieldType = null!;
        return false;
    }

    private int ProducedCount(CilInstruction instruction) => instruction.Operation switch
    {
        CilOperation.Call or CilOperation.CallVirtual =>
            CallSignature(instruction).ReturnType == CliValueKind.Void ? 0 : 1,
        CilOperation.CallIndirect =>
            ((CilOperand.CallSite)instruction.Operand).Signature.ReturnType ==
            CliValueKind.Void ? 0 : 1,
        CilOperation.Nop or CilOperation.Pop or CilOperation.Branch or
            CilOperation.BranchIfTrue or CilOperation.BranchIfFalse or
            CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual or
            CilOperation.BranchIfGreaterThanSigned or
            CilOperation.BranchIfGreaterThanUnsigned or
            CilOperation.BranchIfGreaterThanOrEqualSigned or
            CilOperation.BranchIfGreaterThanOrEqualUnsigned or
            CilOperation.BranchIfLessThanSigned or
            CilOperation.BranchIfLessThanUnsigned or
            CilOperation.BranchIfLessThanOrEqualSigned or
            CilOperation.BranchIfLessThanOrEqualUnsigned or
            CilOperation.Switch or CilOperation.StoreArgument or
            CilOperation.StoreLocal or CilOperation.StoreField or
            CilOperation.StoreStaticField or CilOperation.StoreObject or
            CilOperation.CopyObject or CilOperation.InitializeObject or
            CilOperation.CopyBlock or CilOperation.InitializeBlock or
            CilOperation.InitializeArrayData or
            CilOperation.StoreArrayElementReference or
            CilOperation.StoreArrayElement or
            CilOperation.StoreRectangularArrayElement or
            CilOperation.Constrained or CilOperation.Volatile or
            CilOperation.Readonly or CilOperation.Unaligned or
            CilOperation.Break or CilOperation.Throw or
            CilOperation.Rethrow or CilOperation.Leave or
            CilOperation.EndFinally or CilOperation.EndFilter or
            CilOperation.Return => 0,
        _ => 1,
    };

    private MethodSignatureModel CallSignature(CilInstruction instruction) =>
        instruction.Operand is CilOperand.MethodInstance method
            ? method.Value.Signature
            : _methodRepository.GetMethod(((CilOperand.Entity)instruction.Operand).Key)
                .Signature;

    private static ImmutableArray<CliValueKind> OutgoingKinds(
        StructuredMethod method,
        StructuredBlockDefinition block,
        int instructionIndex)
    {
        if (instructionIndex + 1 < block.Instructions.Length)
        {
            return method.InstructionEntryStacks[
                block.Instructions[instructionIndex + 1].Offset];
        }
        var exitOffset = block.Exit switch
        {
            StructuredBranchExit branch => branch.InstructionOffset,
            StructuredConditionalExit conditional =>
                conditional.Condition.InstructionOffset,
            StructuredLeaveExit leave => leave.InstructionOffset,
            StructuredTerminalExit terminal => terminal.Instruction.Offset,
            _ => (int?)null,
        };
        if (exitOffset is { } offset)
        {
            return method.InstructionEntryStacks[offset];
        }
        var successors = Successors(block.Exit);
        return successors.Length == 0
            ? []
            : method.Blocks[successors[0]].EntryStack;
    }

    private static ImmutableArray<StructuredBlockId> Successors(
        StructuredBlockExit exit) => exit switch
        {
            StructuredFallthroughExit { Target: { } target } => [target],
            StructuredBranchExit branch => [branch.Target],
            StructuredConditionalExit conditional =>
                [conditional.WhenTaken, conditional.WhenNotTaken],
            StructuredLeaveExit leave => [leave.Target],
            _ => [],
        };

    private static ImmutableArray<AddressOrigins> InitialLocals(
        ImmutableArray<CliTypeIdentity> types) =>
        [.. types.Select(type => type.StackKind == CliValueKind.ManagedAddress
            ? AddressOrigins.UnknownOnly
            : AddressOrigins.None)];

    private static AddressOrigins RootOrigins(
        Dictionary<ManagedAggregateStorageId, ManagedAggregateStorage> storages,
        ManagedAggregateStorageKind kind,
        int index)
    {
        var id = new ManagedAggregateStorageId(kind, index);
        return storages.TryGetValue(id, out var storage)
            ? new(false, [new(id, 0, storage.Type)])
            : AddressOrigins.UnknownOnly;
    }

    private static bool ProducesManagedAddress(
        ImmutableArray<CliValueKind> outgoing,
        int produced) => produced != 0 &&
        !outgoing.IsEmpty &&
        outgoing[^1] == CliValueKind.ManagedAddress;

    private static bool IsAddressTransparent(CilOperation operation) => operation is
        CilOperation.Nop or CilOperation.Branch or CilOperation.Break or
        CilOperation.Constrained or CilOperation.Volatile or
        CilOperation.Unaligned or CilOperation.Readonly;

    private static void RejectStoredAddress(
        AddressOrigins origins,
        bool storedAsNonAddress,
        HashSet<ManagedAggregateStorageId> rejected)
    {
        if (storedAsNonAddress)
        {
            Reject(origins, rejected);
        }
    }

    private static void RejectConsumed(
        IReadOnlyList<AddressOrigins> stack,
        int consumed,
        HashSet<ManagedAggregateStorageId> rejected)
    {
        for (var index = stack.Count - consumed; index < stack.Count; index++)
        {
            Reject(stack[index], rejected);
        }
    }

    private static void Reject(
        AddressOrigins origins,
        HashSet<ManagedAggregateStorageId> rejected)
    {
        foreach (var recipe in origins.Recipes)
        {
            Reject(recipe.Storage, rejected);
        }
    }

    private static void Reject(
        ManagedAggregateStorageId storage,
        HashSet<ManagedAggregateStorageId> rejected)
    {
        rejected.Add(storage);
    }

    private static bool TrySingleRecipe(
        AddressOrigins origins,
        HashSet<ManagedAggregateStorageId> rejected,
        out ManagedAggregateAddressRecipe recipe)
    {
        if (!origins.Unknown && origins.Recipes.Count == 1)
        {
            recipe = origins.Recipes.Single();
            return true;
        }
        Reject(origins, rejected);
        recipe = default;
        return false;
    }

    private static IEnumerable<ManagedAggregateStorageId> Storages(FlowState state) =>
        state.Stack.Concat(state.Locals)
            .SelectMany(origins => origins.Recipes)
            .Select(recipe => recipe.Storage)
            .Distinct();

    private static ImmutableArray<AddressOrigins> Union(
        ImmutableArray<AddressOrigins> left,
        ImmutableArray<AddressOrigins> right) =>
        [.. left.Select((origins, index) => origins.Union(right[index]))];

    private readonly record struct AddressOrigins(
        bool Unknown,
        ImmutableHashSet<ManagedAggregateAddressRecipe> Recipes)
    {
        public static AddressOrigins None { get; } = new(false, []);
        public static AddressOrigins UnknownOnly { get; } = new(true, []);

        public AddressOrigins Union(AddressOrigins other) => new(
            Unknown || other.Unknown,
            Recipes.Union(other.Recipes));

    }

    private sealed record FlowState(
        ImmutableArray<AddressOrigins> Stack,
        ImmutableArray<AddressOrigins> Locals);
}
