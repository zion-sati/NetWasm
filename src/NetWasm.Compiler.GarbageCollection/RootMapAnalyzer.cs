using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public sealed class RootMapAnalyzer(
    IValueLayoutProvider layouts,
    IStackStorageTypeAnalyzer stackStorage,
    IStackStorageContinuationAnalyzer continuations,
    IStackStorageAnchorPlanner anchors,
    IRootDecisionClassifier rootDecisions,
    ICalleeArgumentRootingClassifier calleeArguments,
    IRootSlotAllocator rootSlots) :
    IRootMapAnalyzer
{
    private readonly IValueLayoutProvider _layouts =
        layouts ?? throw new ArgumentNullException(nameof(layouts));
    private readonly IStackStorageTypeAnalyzer _stackStorage =
        stackStorage ?? throw new ArgumentNullException(nameof(stackStorage));
    private readonly IStackStorageContinuationAnalyzer _continuations =
        continuations ?? throw new ArgumentNullException(nameof(continuations));
    private readonly IStackStorageAnchorPlanner _anchors =
        anchors ?? throw new ArgumentNullException(nameof(anchors));
    private readonly IRootDecisionClassifier _rootDecisions =
        rootDecisions ?? throw new ArgumentNullException(nameof(rootDecisions));
    private readonly ICalleeArgumentRootingClassifier _calleeArguments =
        calleeArguments ?? throw new ArgumentNullException(nameof(calleeArguments));
    private readonly IRootSlotAllocator _rootSlots =
        rootSlots ?? throw new ArgumentNullException(nameof(rootSlots));

    public MethodRootMap Analyze(RootMapAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Method);
        ArgumentNullException.ThrowIfNull(request.AllocatingMethods);
        ArgumentNullException.ThrowIfNull(request.AllocatingConstructedMethods);
        return AnalyzeCore(
            request.Method,
            request.AllocatingMethods,
            request.AllocatingConstructedMethods);
    }

    private MethodRootMap AnalyzeCore(
        ManagedMethodBody method,
        ISet<EntityKey> allocatingMethods,
        ISet<string> allocatingConstructedMethods)
    {
        var graph = method.ControlFlow.Graph;
        var blockInstructions = graph.Blocks.ToImmutableDictionary(
            block => block.Index,
            block => block.Instructions);
        var instructionBlocks = blockInstructions
            .SelectMany(pair => pair.Value.Select(instruction =>
                (instruction.Offset, Block: pair.Key)))
            .ToImmutableDictionary(pair => pair.Offset, pair => pair.Block);
        var liveAfter = AnalyzeVariableLiveness(method, blockInstructions);
        var persistentManagedAddressArguments =
            GetPersistentManagedAddressArgumentRoots(method);
        var safepoints = new List<SafepointRootMap>();
        var selected = new List<(CilInstruction Instruction, ImmutableArray<CliValueKind> Stack, int CalleeRooted)>();
        foreach (var instruction in method.Body.Instructions)
        {
            if (!method.ControlFlow.InstructionEntryStacks.TryGetValue(
                    instruction.Offset, out var entryStack) ||
                !instructionBlocks.TryGetValue(instruction.Offset, out var block) ||
                !_rootDecisions.Decide(new(
                    method,
                    block,
                    instruction,
                    allocatingMethods,
                    allocatingConstructedMethods)))
            {
                continue;
            }

            var calleeRooted = _calleeArguments.Classify(new(
                method,
                block,
                instruction,
                allocatingMethods,
                allocatingConstructedMethods));
            selected.Add((instruction, entryStack, calleeRooted));
        }
        var storage = selected.Count == 0
            ? StackStorageAnalysis.Empty
            : _stackStorage.Analyze(method);
        var pending = new List<(CilInstruction Instruction, ImmutableHashSet<RootSource> Roots)>();
        var demands = ImmutableDictionary.CreateBuilder<int, ImmutableHashSet<StackStorageAnchor>>();
        foreach (var (instruction, entryStack, calleeRooted) in selected)
        {
            var roots = liveAfter[instruction.Offset].ToBuilder();
            var demanded = ImmutableHashSet.CreateBuilder<StackStorageAnchor>();
            roots.UnionWith(persistentManagedAddressArguments);
            if (storage.Locals.TryGetValue(instruction.Offset, out var localTypes))
            {
                foreach (var localIndex in roots
                             .Where(source => source.Kind == RootSourceKind.ManagedAddressLocal && !source.IsIndirect)
                             .Select(source => source.Index).ToArray())
                {
                    AddBackingOwners(localTypes[localIndex], roots);
                    AddAnchorDemands(method, instruction, localTypes[localIndex],
                        new(StackStorageLocationKind.Local, localIndex), demanded);
                }
            }
            AddStackRoots(roots, entryStack, calleeRooted);
            if (storage.Entries.TryGetValue(instruction.Offset, out var stackTypes))
            {
                AddEmbeddedStackRoots(method, instruction, entryStack, stackTypes, roots);
                for (var index = 0; index < stackTypes.Length; index++)
                {
                    AddAnchorDemands(method, instruction, stackTypes[index],
                        new(StackStorageLocationKind.EvaluationStack, index), demanded);
                }
            }
            pending.Add((instruction, roots.ToImmutable()));
            if (demanded.Count != 0) demands.Add(instruction.Offset, demanded.ToImmutable());
        }

        var anchorPlan = demands.Count == 0 ? StackStorageAnchorPlan.Empty :
            _anchors.Plan(new(method, storage, demands.ToImmutable()));
        var anchorSlots = anchorPlan.Anchors.Select((anchor, index) => (anchor, index))
            .ToDictionary(item => item.anchor, item => item.index);
        foreach (var (instruction, ordinaryRoots) in pending)
        {
            var roots = ordinaryRoots.ToBuilder();
            if (demands.TryGetValue(instruction.Offset, out var demanded))
            {
                foreach (var anchor in demanded)
                {
                    var index = anchorSlots[anchor];
                    roots.Add(new(RootSourceKind.StackStorageAnchor, index));
                    roots.UnionWith(ReferenceSourcesForStorage(RootSourceKind.StackStorageAnchor,
                        index, anchor.BackingType, referenceIsIndirect: true));
                }
            }
            var orderedRoots = Order(roots);
            var constructorRoots = ImmutableArray<RootSource>.Empty;
            if (instruction.Operation == CilOperation.NewObject &&
                IsAllocatingConstructor(
                    instruction,
                    allocatingMethods,
                    allocatingConstructedMethods))
            {
                roots.Add(new RootSource(RootSourceKind.AllocationTemporary, 0));
                constructorRoots = Order(roots);
            }

            safepoints.Add(new SafepointRootMap(
                instruction.Offset,
                orderedRoots,
                constructorRoots));
        }

        return new MethodRootMap(
            method.Method.Definition.Key,
            _rootSlots.Allocate(safepoints),
            safepoints.ToImmutableDictionary(safepoint => safepoint.IlOffset))
        {
            AnchorPlan = anchorPlan,
        };
    }

    private ImmutableDictionary<int, ImmutableHashSet<RootSource>>
        AnalyzeVariableLiveness(
            ManagedMethodBody method,
            ImmutableDictionary<int, ImmutableArray<CilInstruction>> blockInstructions)
    {
        var graph = method.ControlFlow.Graph;
        var flow = _continuations.Analyze(method);
        var blockLiveIn = graph.Blocks.ToDictionary(
            block => block.Index,
            _ => ImmutableHashSet<RootSource>.Empty);
        var liveAfter = new Dictionary<int, ImmutableHashSet<RootSource>>();
        var blocks = graph.Blocks
            .OrderByDescending(block => block.Index)
            .ToArray();
        bool changed;
        do
        {
            changed = false;
            foreach (var block in blocks)
            {
                var live = flow.Normal[block.Index]
                    .SelectMany(successor => blockLiveIn[successor.Block])
                    .ToImmutableHashSet();
                var instructions = blockInstructions[block.Index];
                for (var index = instructions.Length - 1; index >= 0; index--)
                {
                    var instruction = instructions[index];
                    if (CilSafepointClassifier.MayTransferControlExceptionally(instruction))
                    {
                        // A throwing filter is rejected before later stores run.
                        live = live.Union(flow.Exceptional[block.Index]
                            .SelectMany(successor => blockLiveIn[successor.Block]));
                    }
                    liveAfter[instruction.Offset] = live;
                    live = Transfer(method, instruction, live);
                }

                if (!blockLiveIn[block.Index].SetEquals(live))
                {
                    blockLiveIn[block.Index] = live;
                    changed = true;
                }
            }
        }
        while (changed);

        return liveAfter.ToImmutableDictionary();
    }

    private ImmutableHashSet<RootSource> Transfer(
        ManagedMethodBody method,
        CilInstruction instruction,
        ImmutableHashSet<RootSource> liveAfter)
    {
        if (instruction.Operand is not CilOperand.Index index)
        {
            return liveAfter;
        }

        var sources = instruction.Operation switch
        {
            CilOperation.LoadArgument or CilOperation.LoadArgumentAddress or
                CilOperation.StoreArgument =>
                SourcesFor(
                    RootSourceKind.Argument,
                    index.Value,
                    GetParameterSignatureTypes(method)[index.Value]),
            CilOperation.LoadLocal or CilOperation.LoadLocalAddress or
                CilOperation.StoreLocal =>
                SourcesFor(
                    RootSourceKind.Local,
                    index.Value,
                    method.Body.LocalSignatureTypes[index.Value]),
            _ => [],
        };
        if (sources.IsEmpty)
        {
            return liveAfter;
        }
        return instruction.Operation is CilOperation.StoreLocal or CilOperation.StoreArgument
            ? liveAfter.Except(sources)
            : liveAfter.Union(sources);
    }

    private static bool IsAllocatingConstructor(
        CilInstruction instruction,
        ISet<EntityKey> allocatingMethods,
        ISet<string> allocatingConstructedMethods)
    {
        if (instruction.Operand is CilOperand.Entity constructor)
        {
            return allocatingMethods.Contains(constructor.Key);
        }

        var constructorMethod = ((CilOperand.MethodInstance)instruction.Operand).Value;
        return constructorMethod.IsConstructed
            ? allocatingConstructedMethods.Contains(constructorMethod.CanonicalName)
            : allocatingMethods.Contains(constructorMethod.Definition.Key);
    }

    private static ImmutableArray<CliTypeIdentity> GetParameterSignatureTypes(
        ManagedMethodBody method)
    {
        var instance = method.Method;
        var definition = instance.Definition;
        var signature = instance.Signature;
        if (definition.IsStatic)
        {
            return signature.ParameterSignatureTypes;
        }
        var declaringType = instance.DeclaringType;
        return signature.ParameterSignatureTypes.Insert(0, declaringType);
    }

    private ImmutableArray<RootSource> GetPersistentManagedAddressArgumentRoots(
        ManagedMethodBody method) =>
        [.. GetParameterSignatureTypes(method)
            .SelectMany((type, index) => type.StackKind == CliValueKind.ManagedAddress
                ? SourcesFor(RootSourceKind.Argument, index, type)
                : [])];

    private ImmutableArray<RootSource> SourcesFor(
        RootSourceKind kind,
        int index,
        CliTypeIdentity type)
    {
        if (type.StackKind != CliValueKind.ManagedAddress)
        {
            return ReferenceSourcesForStorage(
                kind,
                index,
                type,
                referenceIsIndirect: false);
        }

        var addressKind = AddressKind(kind);
        var elementType = type.ElementType!;
        return
        [
            new RootSource(addressKind, index),
            .. ReferenceSourcesForStorage(
                addressKind,
                index,
                elementType,
                referenceIsIndirect: true),
        ];
    }

    private void AddEmbeddedStackRoots(
        ManagedMethodBody method,
        CilInstruction instruction,
        ImmutableArray<CliValueKind> stack,
        ImmutableArray<StackStorageType> stackTypes,
        ImmutableHashSet<RootSource>.Builder roots)
    {
        for (var stackIndex = 0; stackIndex < stackTypes.Length; stackIndex++)
        {
            AddBackingOwners(stackTypes[stackIndex], roots);
            if (stackTypes[stackIndex].Type is not { } type)
            {
                if (!stackTypes[stackIndex].BackingCovered && !stackTypes[stackIndex].CompleteBackingKnown &&
                    stack[stackIndex] is CliValueKind.ValueType or CliValueKind.ManagedAddress)
                {
                    throw new CompilerException(new CompilerDiagnostic(DiagnosticCode.InvalidCil,
                        "live stack storage identity is unresolved at a collection safepoint",
                        method.Method.Definition.Name, instruction.Offset));
                }
                continue;
            }
            var storageType = type;
            var referenceIsIndirect = false;
            if (type.StackKind == CliValueKind.ManagedAddress)
            {
                storageType = type.ElementType!;
                referenceIsIndirect = true;
            }

            foreach (var source in ReferenceSourcesForStorage(
                         RootSourceKind.EvaluationStack,
                         stackIndex,
                         storageType,
                         referenceIsIndirect))
            {
                roots.Add(source);
            }
        }
    }

    private void AddAnchorDemands(
        ManagedMethodBody method,
        CilInstruction instruction,
        StackStorageType storage,
        StackStorageLocation location,
        ImmutableHashSet<StackStorageAnchor>.Builder demands)
    {
        if (location.Kind == StackStorageLocationKind.Local && storage.Type is null &&
            !storage.BackingCovered && !storage.CompleteBackingKnown)
        {
            throw new CompilerException(new CompilerDiagnostic(DiagnosticCode.InvalidCil,
                "live storage identity is unresolved at a collection safepoint",
                method.Method.Definition.Name, instruction.Offset));
        }
        foreach (var backing in storage.CapturedBackings)
        {
            var pointee = storage.Type?.StackKind == CliValueKind.ManagedAddress
                ? storage.Type.ElementType : storage.Type;
            if (backing.Equals(pointee) || ReferenceSourcesForStorage(
                    RootSourceKind.StackStorageAnchor, 0, backing, referenceIsIndirect: true).IsEmpty)
                continue;
            if (!storage.CompleteBackingKnown)
            {
                throw new CompilerException(new CompilerDiagnostic(DiagnosticCode.InvalidCil,
                    "live storage backing is incomplete at a collection safepoint",
                    method.Method.Definition.Name, instruction.Offset));
            }
            demands.Add(new(location, backing));
        }
    }

    private void AddBackingOwners(StackStorageType storage, ImmutableHashSet<RootSource>.Builder roots)
    {
        foreach (var owner in storage.BackingOwners)
        {
            if (storage.Type is { StackKind: CliValueKind.ManagedAddress } address &&
                address.ElementType!.Equals(owner.Type.StackKind == CliValueKind.ManagedAddress
                    ? owner.Type.ElementType! : owner.Type))
            {
                continue;
            }
            roots.UnionWith(SourcesFor(owner.IsArgument ? RootSourceKind.Argument : RootSourceKind.Local,
                owner.Index, owner.Type));
        }
    }

    private ImmutableArray<RootSource> ReferenceSourcesForStorage(
        RootSourceKind kind,
        int index,
        CliTypeIdentity type,
        bool referenceIsIndirect) => type.StackKind switch
        {
            CliValueKind.ManagedReference =>
            [new RootSource(kind, index, referenceIsIndirect ? 0 : -1)],
            CliValueKind.ValueType =>
            [.. _layouts.GetValueLayout(type).ReferenceOffsets.Select(
                offset => new RootSource(kind, index, offset))],
            _ => [],
        };

    private static void AddStackRoots(
        ImmutableHashSet<RootSource>.Builder roots,
        ImmutableArray<CliValueKind> stack,
        int calleeRooted)
    {
        for (var index = 0; index < stack.Length; index++)
        {
            // The callee roots the object references it takes; addresses keep
            // their roots for the storage they point into.
            if (stack[index] == CliValueKind.ManagedReference && index < stack.Length - calleeRooted)
            {
                roots.Add(new RootSource(RootSourceKind.EvaluationStack, index));
            }
            else if (stack[index] == CliValueKind.ManagedAddress)
            {
                roots.Add(new RootSource(
                    RootSourceKind.ManagedAddressEvaluationStack,
                    index));
            }
        }
    }

    private static RootSourceKind AddressKind(RootSourceKind kind) =>
        kind == RootSourceKind.Argument
            ? RootSourceKind.ManagedAddressArgument
            : RootSourceKind.ManagedAddressLocal;

    private static ImmutableArray<RootSource> Order(
        IEnumerable<RootSource> roots) => [
        ..roots
            .OrderBy(source => source.Kind)
            .ThenBy(source => source.Index)
            .ThenBy(source => source.ByteOffset)
    ];
}
