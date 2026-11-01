using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

// Strategy: propagate only argument-backed managed-address aliases. Any use that
// is not proved read-only is conservatively classified as write-and-escape.
public sealed class ManagedByRefEffectAnalyzer(
    IMethodRepository methods,
    IRuntimeIntrinsicRegistry intrinsics) : IManagedByRefEffectAnalyzer
{
    private readonly IMethodRepository _methods = methods ??
        throw new ArgumentNullException(nameof(methods));
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ??
        throw new ArgumentNullException(nameof(intrinsics));

    public ManagedByRefEffects Analyze(ManagedByRefEffectAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Methods);
        ArgumentNullException.ThrowIfNull(request.ConstructedMethods);
        ArgumentNullException.ThrowIfNull(request.DispatchCallSites);

        var directIdentities = request.Methods.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Method.CanonicalName);
        var bodies = request.Methods
            .Select(pair => (Identity: directIdentities[pair.Key], pair.Value))
            .Concat(request.ConstructedMethods.Select(pair =>
                (Identity: pair.Key, pair.Value)))
            .ToDictionary(pair => pair.Identity, pair => pair.Value,
                StringComparer.Ordinal);
        var effects = bodies.Keys.ToDictionary(
            identity => identity,
            _ => ManagedByRefMethodEffects.Empty,
            StringComparer.Ordinal);

        bool changed;
        do
        {
            changed = false;
            foreach ((var identity, var body) in bodies)
            {
                var analyzed = AnalyzeBody(
                    identity,
                    body,
                    effects,
                    bodies,
                    directIdentities,
                    request.DispatchCallSites);
                var combined = effects[identity].Union(analyzed);
                if (combined != effects[identity])
                {
                    effects[identity] = combined;
                    changed = true;
                }
            }
        }
        while (changed);

        return new(effects.ToImmutableDictionary(StringComparer.Ordinal));
    }

    private ManagedByRefMethodEffects AnalyzeBody(
        string identity,
        ManagedMethodBody method,
        IReadOnlyDictionary<string, ManagedByRefMethodEffects> effects,
        IReadOnlyDictionary<string, ManagedMethodBody> bodies,
        IReadOnlyDictionary<EntityKey, string> directIdentities,
        IReadOnlyDictionary<string, DispatchCallSiteModel> dispatchCallSites)
    {
        var written = ImmutableHashSet.CreateBuilder<int>();
        var escaped = ImmutableHashSet.CreateBuilder<int>();
        var returned = ImmutableHashSet.CreateBuilder<int>();
        var candidates = CandidateArguments(method);

        // Exception handlers can observe byref locals established on exceptional
        // edges. Until that flow is modeled, retaining ownership is the only safe
        // answer for an address-taken or byref argument in such a method.
        if (!method.Body.ExceptionRegions.IsEmpty)
        {
            written.UnionWith(candidates);
            escaped.UnionWith(candidates);
            return new(written.ToImmutable(), escaped.ToImmutable(), returned.ToImmutable());
        }

        var graph = method.ControlFlow.Graph;
        var initialLocals = method.Body.LocalSignatureTypes
            .Select(_ => ImmutableHashSet<int>.Empty)
            .ToImmutableArray();
        var entries = new Dictionary<int, FlowState>();
        var pending = new Queue<int>();
        Merge(graph.Entry.Index, new([], initialLocals));

        while (pending.TryDequeue(out var blockIndex))
        {
            var block = graph.GetBlock(blockIndex);
            var stack = entries[blockIndex].Stack.ToList();
            var locals = entries[blockIndex].Locals.ToBuilder();
            for (var instructionIndex = 0;
                 instructionIndex < block.Instructions.Length;
                 instructionIndex++)
            {
                var instruction = block.Instructions[instructionIndex];
                var entryKinds = method.ControlFlow.InstructionEntryStacks[instruction.Offset];
                var consumedOrigins = ConsumedOrigins(
                    method,
                    block,
                    instructionIndex,
                    instruction,
                    stack);
                var produced = ProducedCount(instruction);
                var producedOrigins = ImmutableHashSet<int>.Empty;

                switch (instruction.Operation)
                {
                    case CilOperation.LoadArgumentAddress:
                        producedOrigins = [ReadIndex(instruction)];
                        break;
                    case CilOperation.LoadArgument:
                        {
                            var argument = ReadIndex(instruction);
                            if (ArgumentType(method, argument).StackKind == CliValueKind.ManagedAddress)
                            {
                                producedOrigins = [argument];
                            }
                            break;
                        }
                    case CilOperation.LoadLocal:
                        {
                            var local = ReadIndex(instruction);
                            if (method.Body.LocalSignatureTypes[local].StackKind ==
                                CliValueKind.ManagedAddress)
                            {
                                producedOrigins = locals[local];
                            }
                            break;
                        }
                    case CilOperation.LoadFieldAddress:
                    case CilOperation.LoadArrayElementAddress:
                    case CilOperation.LoadRectangularArrayElementAddress:
                        producedOrigins = stack[^1];
                        break;
                    case CilOperation.Add:
                    case CilOperation.Subtract:
                        if (ProducedKind(method, block, instructionIndex) ==
                            CliValueKind.ManagedAddress)
                        {
                            producedOrigins = consumedOrigins
                                .Aggregate(ImmutableHashSet<int>.Empty,
                                    (current, origins) => current.Union(origins));
                        }
                        break;
                    case CilOperation.Duplicate:
                        producedOrigins = stack[^1];
                        break;
                    case CilOperation.Call:
                    case CilOperation.CallVirtual:
                    case CilOperation.NewObject:
                        producedOrigins = ApplyManagedCall(
                            identity,
                            instruction,
                            stack,
                            written,
                            escaped,
                            effects,
                            bodies,
                            directIdentities,
                            dispatchCallSites);
                        break;
                    case CilOperation.CallIndirect:
                        MarkUnknown(consumedOrigins, written, escaped);
                        break;
                    case CilOperation.StoreObject:
                    case CilOperation.CopyObject:
                        MarkWritten(stack[^2], written);
                        if (instruction.Operation == CilOperation.StoreObject)
                        {
                            MarkEscaped(stack[^1], escaped);
                        }
                        break;
                    case CilOperation.InitializeObject:
                        MarkWritten(stack[^1], written);
                        break;
                    case CilOperation.CopyBlock:
                    case CilOperation.InitializeBlock:
                        MarkWritten(stack[^3], written);
                        break;
                    case CilOperation.StoreField:
                        MarkWritten(stack[^2], written);
                        MarkEscaped(stack[^1], escaped);
                        break;
                    case CilOperation.StoreStaticField:
                        MarkEscaped(stack[^1], escaped);
                        break;
                    case CilOperation.StoreArrayElement:
                    case CilOperation.StoreArrayElementReference:
                    case CilOperation.StoreRectangularArrayElement:
                        MarkEscaped(stack[^1], escaped);
                        break;
                    case CilOperation.CompareExchange:
                        MarkUnknown(consumedOrigins, written, escaped);
                        break;
                    case CilOperation.Return:
                        if (method.Method.Signature.ReturnType ==
                            CliValueKind.ManagedAddress && stack.Count != 0)
                        {
                            returned.UnionWith(stack[^1]);
                        }
                        break;
                    default:
                        if (!IsProvedReadOnlyConsumer(instruction.Operation))
                        {
                            MarkUnknown(consumedOrigins, written, escaped);
                        }
                        break;
                }

                if (instruction.Operation == CilOperation.StoreLocal &&
                    method.Body.LocalSignatureTypes[ReadIndex(instruction)]
                        .StackKind == CliValueKind.ManagedAddress)
                {
                    locals[ReadIndex(instruction)] = stack[^1];
                }

                if (instruction.Operation == CilOperation.Duplicate)
                {
                    stack.Add(producedOrigins);
                    continue;
                }

                var outgoing = OutgoingKinds(method, block, instructionIndex);
                var preserved = outgoing.Length - produced;
                stack.RemoveRange(preserved, stack.Count - preserved);
                if (produced != 0)
                {
                    stack.Add(producedOrigins);
                }
            }

            foreach (var successor in graph.Successors[blockIndex])
            {
                Merge(successor, new([.. stack], locals.ToImmutable()));
            }
        }

        return new(written.ToImmutable(), escaped.ToImmutable(), returned.ToImmutable());

        void Merge(int block, FlowState incoming)
        {
            if (!entries.TryGetValue(block, out var existing))
            {
                entries.Add(block, incoming);
                pending.Enqueue(block);
                return;
            }

            var stack = Union(existing.Stack, incoming.Stack);
            var locals = Union(existing.Locals, incoming.Locals);
            if (stack.AsSpan().SequenceEqual(existing.Stack.AsSpan()) &&
                locals.AsSpan().SequenceEqual(existing.Locals.AsSpan()))
            {
                return;
            }
            entries[block] = new(stack, locals);
            pending.Enqueue(block);
        }
    }

    private ImmutableHashSet<int> ApplyManagedCall(
        string caller,
        CilInstruction instruction,
        List<ImmutableHashSet<int>> stack,
        ImmutableHashSet<int>.Builder written,
        ImmutableHashSet<int>.Builder escaped,
        IReadOnlyDictionary<string, ManagedByRefMethodEffects> effects,
        IReadOnlyDictionary<string, ManagedMethodBody> bodies,
        IReadOnlyDictionary<EntityKey, string> directIdentities,
        IReadOnlyDictionary<string, DispatchCallSiteModel> dispatchCallSites)
    {
        var call = ResolveCall(instruction, stack);
        if (call.Definition is not null &&
            _intrinsics.TryGetIntrinsic(call.Definition.Key, out var intrinsic) &&
            IsAliasPreservingIntrinsic(intrinsic))
        {
            return call.Arguments.Values
                .Aggregate(ImmutableHashSet<int>.Empty,
                    (current, origins) => current.Union(origins));
        }

        var targets = ResolveTargets(
            caller,
            instruction,
            call,
            bodies,
            directIdentities,
            dispatchCallSites);
        if (targets is null || targets.Count == 0)
        {
            MarkUnknown(call.Arguments.Values, written, escaped);
            return [];
        }

        var result = ImmutableHashSet<int>.Empty;
        foreach (var target in targets)
        {
            if (!effects.TryGetValue(target, out var targetEffects))
            {
                MarkUnknown(call.Arguments.Values, written, escaped);
                return [];
            }
            foreach (var argument in targetEffects.WrittenArguments)
            {
                if (call.Arguments.TryGetValue(argument, out var origins))
                {
                    written.UnionWith(origins);
                }
            }
            foreach (var argument in targetEffects.EscapedArguments)
            {
                if (call.Arguments.TryGetValue(argument, out var origins))
                {
                    escaped.UnionWith(origins);
                }
            }
            foreach (var argument in targetEffects.ReturnedArgumentAliases)
            {
                if (call.Arguments.TryGetValue(argument, out var origins))
                {
                    result = result.Union(origins);
                }
            }
        }
        return result;
    }

    private static IReadOnlyCollection<string>? ResolveTargets(
        string caller,
        CilInstruction instruction,
        CallModel call,
        IReadOnlyDictionary<string, ManagedMethodBody> bodies,
        IReadOnlyDictionary<EntityKey, string> directIdentities,
        IReadOnlyDictionary<string, DispatchCallSiteModel> dispatchCallSites)
    {
        if (instruction.Operation == CilOperation.CallVirtual)
        {
            var dispatchKey = $"{caller}@{instruction.Offset:x8}";
            return dispatchCallSites.TryGetValue(dispatchKey, out var dispatch) &&
                   !dispatch.Targets.IsEmpty
                ? [.. dispatch.Targets.Select(target => target.Method.CanonicalName)]
                : null;
        }
        if (call.Identity is { } identity && bodies.ContainsKey(identity))
        {
            return [identity];
        }
        if (call.Definition is not null &&
            directIdentities.TryGetValue(call.Definition.Key, out var direct))
        {
            return [direct];
        }
        return null;
    }

    private CallModel ResolveCall(
        CilInstruction instruction,
        List<ImmutableHashSet<int>> stack)
    {
        MethodDefinitionModel definition;
        MethodSignatureModel signature;
        string? identity;
        if (instruction.Operand is CilOperand.MethodInstance instance)
        {
            definition = instance.Value.Definition;
            signature = instance.Value.Signature;
            identity = instance.Value.CanonicalName;
        }
        else
        {
            definition = _methods.GetMethod(
                ((CilOperand.Entity)instruction.Operand).Key);
            signature = definition.Signature;
            identity = null;
        }

        var isNewObject = instruction.Operation == CilOperation.NewObject;
        var hasReceiver = !definition.IsStatic && !isNewObject;
        var consumed = signature.ParameterTypes.Length + (hasReceiver ? 1 : 0);
        var first = stack.Count - consumed;
        var arguments = ImmutableDictionary.CreateBuilder<int, ImmutableHashSet<int>>();
        if (hasReceiver)
        {
            arguments.Add(0, stack[first]);
        }
        var parameterOffset = definition.IsStatic ? 0 : 1;
        for (var index = 0; index < signature.ParameterTypes.Length; index++)
        {
            arguments.Add(index + parameterOffset,
                stack[first + index + (hasReceiver ? 1 : 0)]);
        }
        return new(definition, identity, arguments.ToImmutable());
    }

    private static ImmutableHashSet<int> CandidateArguments(ManagedMethodBody method)
    {
        var candidates = method.Body.Instructions
            .Where(instruction => instruction.Operation ==
                CilOperation.LoadArgumentAddress)
            .Select(ReadIndex)
            .ToImmutableHashSet()
            .ToBuilder();
        var count = method.Method.Signature.ParameterTypes.Length +
            (method.Method.Definition.IsStatic ? 0 : 1);
        for (var index = 0; index < count; index++)
        {
            if (ArgumentType(method, index).StackKind == CliValueKind.ManagedAddress)
            {
                candidates.Add(index);
            }
        }
        return candidates.ToImmutable();
    }

    private static CliTypeIdentity ArgumentType(ManagedMethodBody method, int index)
    {
        if (!method.Method.Definition.IsStatic && index == 0)
        {
            return method.Method.DeclaringType.IsValueType
                ? CliTypeIdentity.ManagedByReference(method.Method.DeclaringType)
                : method.Method.DeclaringType;
        }
        return method.Method.Signature.ParameterSignatureTypes[
            index - (method.Method.Definition.IsStatic ? 0 : 1)];
    }

    private ImmutableArray<ImmutableHashSet<int>> ConsumedOrigins(
        ManagedMethodBody method,
        BasicBlock block,
        int instructionIndex,
        CilInstruction instruction,
        List<ImmutableHashSet<int>> stack)
    {
        var produced = ProducedCount(instruction);
        var outgoing = OutgoingKinds(method, block, instructionIndex);
        var consumed = stack.Count + produced - outgoing.Length;
        return consumed == 0 ? [] : [.. stack.Skip(stack.Count - consumed)];
    }

    private static ImmutableArray<CliValueKind> OutgoingKinds(
        ManagedMethodBody method,
        BasicBlock block,
        int instructionIndex)
    {
        if (instructionIndex + 1 < block.Instructions.Length)
        {
            return method.ControlFlow.InstructionEntryStacks[
                block.Instructions[instructionIndex + 1].Offset];
        }
        var successors = method.ControlFlow.Graph.Successors[block.Index];
        return successors.IsEmpty ? [] : method.ControlFlow.EntryStacks[successors[0]];
    }

    private static CliValueKind ProducedKind(
        ManagedMethodBody method,
        BasicBlock block,
        int instructionIndex)
    {
        var outgoing = OutgoingKinds(method, block, instructionIndex);
        return outgoing[^1];
    }

    private int ProducedCount(CilInstruction instruction) =>
        instruction.Operation switch
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
                CilOperation.StoreArgument or
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
            : _methods.GetMethod(((CilOperand.Entity)instruction.Operand).Key)
                .Signature;

    private static int ReadIndex(CilInstruction instruction) =>
        ((CilOperand.Index)instruction.Operand).Value;

    private static bool IsAliasPreservingIntrinsic(RuntimeIntrinsic intrinsic) =>
        intrinsic is RuntimeIntrinsic.UnsafeAdd or
            RuntimeIntrinsic.UnsafeAs or
            RuntimeIntrinsic.UnsafeAsRef or
            RuntimeIntrinsic.UnsafeAsRefManaged or
            RuntimeIntrinsic.UnsafeAddByteOffset;

    private static bool IsProvedReadOnlyConsumer(CilOperation operation) =>
        operation is CilOperation.Nop or CilOperation.Pop or
            CilOperation.LoadObject or CilOperation.LoadField or
            CilOperation.LoadFieldAddress or CilOperation.LoadArrayElementAddress or
            CilOperation.LoadRectangularArrayElementAddress or
            CilOperation.Add or CilOperation.Subtract or
            CilOperation.CompareEqual or CilOperation.CompareGreaterThanSigned or
            CilOperation.CompareGreaterThanUnsigned or
            CilOperation.CompareLessThanSigned or
            CilOperation.CompareLessThanUnsigned or
            CilOperation.Branch or CilOperation.BranchIfTrue or
            CilOperation.BranchIfFalse or CilOperation.BranchIfEqual or
            CilOperation.BranchIfNotEqual or
            CilOperation.BranchIfGreaterThanSigned or
            CilOperation.BranchIfGreaterThanUnsigned or
            CilOperation.BranchIfGreaterThanOrEqualSigned or
            CilOperation.BranchIfGreaterThanOrEqualUnsigned or
            CilOperation.BranchIfLessThanSigned or
            CilOperation.BranchIfLessThanUnsigned or
            CilOperation.BranchIfLessThanOrEqualSigned or
            CilOperation.BranchIfLessThanOrEqualUnsigned or
            CilOperation.StoreArgument or
            CilOperation.StoreLocal or CilOperation.Readonly or
            CilOperation.Volatile or CilOperation.Unaligned or
            CilOperation.Constrained or CilOperation.Leave or
            CilOperation.EndFinally or CilOperation.EndFilter or
            CilOperation.Throw or CilOperation.Rethrow;

    private static ImmutableArray<ImmutableHashSet<int>> Union(
        ImmutableArray<ImmutableHashSet<int>> first,
        ImmutableArray<ImmutableHashSet<int>> second)
    {
        return [.. first.Select((origins, index) => origins.Union(second[index]))];
    }

    private static void MarkWritten(
        IEnumerable<int> origins,
        ImmutableHashSet<int>.Builder written) => written.UnionWith(origins);

    private static void MarkEscaped(
        IEnumerable<int> origins,
        ImmutableHashSet<int>.Builder escaped) => escaped.UnionWith(origins);

    private static void MarkUnknown(
        IEnumerable<IEnumerable<int>> origins,
        ImmutableHashSet<int>.Builder written,
        ImmutableHashSet<int>.Builder escaped)
    {
        foreach (var origin in origins)
        {
            written.UnionWith(origin);
            escaped.UnionWith(origin);
        }
    }

    private sealed record FlowState(
        ImmutableArray<ImmutableHashSet<int>> Stack,
        ImmutableArray<ImmutableHashSet<int>> Locals);

    private sealed record CallModel(
        MethodDefinitionModel? Definition,
        string? Identity,
        ImmutableDictionary<int, ImmutableHashSet<int>> Arguments);
}
