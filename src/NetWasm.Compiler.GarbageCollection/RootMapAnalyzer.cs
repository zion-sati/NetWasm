using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public sealed class RootMapAnalyzer(
    ITypeRepository types,
    IMethodRepository methods,
    IValueLayoutProvider layouts,
    IRootDecisionClassifier rootDecisions) :
    IRootMapAnalyzer
{
    private readonly ITypeRepository _types =
        types ?? throw new ArgumentNullException(nameof(types));
    private readonly IMethodRepository _methods =
        methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly IValueLayoutProvider _layouts =
        layouts ?? throw new ArgumentNullException(nameof(layouts));
    private readonly IRootDecisionClassifier _rootDecisions =
        rootDecisions ?? throw new ArgumentNullException(nameof(rootDecisions));

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

            var roots = liveAfter[instruction.Offset].ToBuilder();
            roots.UnionWith(persistentManagedAddressArguments);
            AddStackRoots(roots, entryStack);
            AddEmbeddedStackRoots(method, instruction, entryStack, roots);
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

        var sources = safepoints
            .SelectMany(safepoint => safepoint.Roots.Concat(safepoint.ConstructorCallRoots))
            .Distinct()
            .OrderBy(source => source.Kind)
            .ThenBy(source => source.Index)
            .ThenBy(source => source.ByteOffset)
            .ToArray();
        var slots = sources
            .Select((source, index) => (source, index))
            .ToImmutableDictionary(item => item.source, item => item.index);
        return new MethodRootMap(
            method.Method.Definition.Key,
            slots,
            safepoints.ToImmutableDictionary(safepoint => safepoint.IlOffset));
    }

    private ImmutableDictionary<int, ImmutableHashSet<RootSource>>
        AnalyzeVariableLiveness(
            ManagedMethodBody method,
            ImmutableDictionary<int, ImmutableArray<CilInstruction>> blockInstructions)
    {
        var graph = method.ControlFlow.Graph;
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
                var live = graph.Successors[block.Index]
                    .SelectMany(successor => blockLiveIn[successor])
                    .ToImmutableHashSet();
                var instructions = blockInstructions[block.Index];
                for (var index = instructions.Length - 1; index >= 0; index--)
                {
                    var instruction = instructions[index];
                    if (CilSafepointClassifier.MayTransferControlExceptionally(instruction))
                    {
                        live = live.Union(graph.ExceptionalSuccessors[block.Index]
                            .SelectMany(successor => blockLiveIn[successor]));
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
        ImmutableArray<CliValueKind> entryStack,
        ImmutableHashSet<RootSource>.Builder roots)
    {
        if (instruction.Operation == CilOperation.Box)
        {
            AddEmbedded(entryStack.Length - 1, GetTypeOperand(instruction));
            return;
        }
        if (instruction.Operation is not (
                CilOperation.Call or CilOperation.CallVirtual or CilOperation.NewObject))
        {
            return;
        }
        (var target, var signature) = instruction.Operand is CilOperand.MethodInstance methodInstance
            ? (methodInstance.Value.Definition, methodInstance.Value.Signature)
            : GetEntityMethod((CilOperand.Entity)instruction.Operand);
        var consumed = signature.ParameterSignatureTypes.Length +
                       (instruction.Operation == CilOperation.NewObject || target.IsStatic ? 0 : 1);
        var first = entryStack.Length - consumed;
        var parameterBase = instruction.Operation == CilOperation.NewObject ? first :
            first + (target.IsStatic ? 0 : 1);
        for (var index = 0; index < signature.ParameterSignatureTypes.Length; index++)
        {
            AddEmbedded(parameterBase + index, signature.ParameterSignatureTypes[index]);
        }
        if (!target.IsStatic && instruction.Operation != CilOperation.NewObject &&
            first >= 0 &&
            entryStack[first] is (CliValueKind.ValueType or CliValueKind.ManagedAddress))
        {
            var declaringType = instruction.Operand is CilOperand.MethodInstance receiverMethodInstance
                ? receiverMethodInstance.Value.DeclaringType
                : GetNamedTypeIdentity(target.DeclaringType);
            if (declaringType.IsValueType)
            {
                AddEmbedded(first, declaringType);
            }
        }

        void AddEmbedded(int stackIndex, CliTypeIdentity type)
        {
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

        (MethodDefinitionModel Definition, MethodSignatureModel Signature) GetEntityMethod(
            CilOperand.Entity entity)
        {
            var definition = _methods.GetMethod(entity.Key);
            return (definition, definition.Signature);
        }
    }

    private CliTypeIdentity GetNamedTypeIdentity(EntityKey key)
    {
        var definition = _types.GetTypeDefinition(key);
        return CliTypeIdentity.Named(
            key.Assembly,
            definition.Namespace,
            definition.Name,
            definition.IsValueType);
    }

    private static CliTypeIdentity GetTypeOperand(CilInstruction instruction) =>
        ((CilOperand.TypeIdentity)instruction.Operand).Value;

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
        ImmutableArray<CliValueKind> stack)
    {
        for (var index = 0; index < stack.Length; index++)
        {
            if (stack[index] == CliValueKind.ManagedReference)
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
