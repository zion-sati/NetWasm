using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

internal sealed class AddressTakenScalarPromotionPlanner :
    IAddressTakenScalarPromotionPlanner
{
    public AddressTakenScalarPromotionPlan Plan(
        AddressTakenScalarPromotionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Method);
        ArgumentNullException.ThrowIfNull(request.CallSites);
        ArgumentNullException.ThrowIfNull(request.ByRefEffects);

        var method = request.Method;
        if (!request.Enabled || !method.ExceptionGroups.IsEmpty)
        {
            return AddressTakenScalarPromotionPlan.Empty;
        }

        var reachableOffsets = method.InstructionEntryStacks.Keys.ToImmutableHashSet();
        var promoted = method.Header.Instructions
            .Where(instruction =>
                reachableOffsets.Contains(instruction.Offset) &&
                instruction.Operation == CilOperation.LoadLocalAddress)
            .Select(CilOperandReader.GetIndex)
            .Where(index =>
                (uint)index < (uint)method.Header.LocalSignatureTypes.Length &&
                IsPromotable(method.Header.LocalSignatureTypes[index].StackKind))
            .Distinct()
            .ToDictionary(index => index, index => method.Header.LocalSignatureTypes[index]);
        if (promoted.Count == 0)
        {
            return AddressTakenScalarPromotionPlan.Empty;
        }

        var rejected = new HashSet<int>();
        var synchronizations = new Dictionary<int, MutableSynchronization>();
        foreach (var block in method.Blocks.Values.OrderBy(block => block.StartOffset))
        {
            var instructions = block.Instructions
                .Where(instruction => reachableOffsets.Contains(instruction.Offset))
                .ToImmutableArray();
            if (instructions.IsEmpty)
            {
                continue;
            }

            var origins = EmptyOrigins(
                method.InstructionEntryStacks[instructions[0].Offset].Length);
            for (var index = 0; index < instructions.Length; index++)
            {
                var instruction = instructions[index];
                var entryCount = method.InstructionEntryStacks[instruction.Offset].Length;
                if (origins.Count != entryCount)
                {
                    RejectAll(origins, rejected);
                    origins = EmptyOrigins(entryCount);
                }

                if (instruction.Operation == CilOperation.LoadLocalAddress)
                {
                    var local = CilOperandReader.GetIndex(instruction);
                    origins.Add(promoted.ContainsKey(local) ? [local] : []);
                    continue;
                }

                if (instruction.Operation == CilOperation.Duplicate)
                {
                    origins.Add(origins[^1]);
                    continue;
                }

                if (instruction.Operation == CilOperation.Call)
                {
                    ApplyDirectCall(
                        request,
                        instruction,
                        origins,
                        rejected,
                        synchronizations);
                    continue;
                }

                if (!ContainsOrigins(origins))
                {
                    origins = index + 1 < instructions.Length
                        ? EmptyOrigins(method.InstructionEntryStacks[
                            instructions[index + 1].Offset].Length)
                        : [];
                    continue;
                }

                if (index + 1 >= instructions.Length)
                {
                    RejectAll(origins, rejected);
                    origins.Clear();
                    continue;
                }

                var outgoingCount = method.InstructionEntryStacks[
                    instructions[index + 1].Offset].Length;
                var producedCount = ProducedCount(instruction);
                var consumedCount = origins.Count + producedCount - outgoingCount;
                if (consumedCount < 0 || consumedCount > origins.Count)
                {
                    RejectAll(origins, rejected);
                    origins = EmptyOrigins(outgoingCount);
                    continue;
                }

                for (var stackIndex = origins.Count - consumedCount;
                     stackIndex < origins.Count;
                     stackIndex++)
                {
                    rejected.UnionWith(origins[stackIndex]);
                }
                origins.RemoveRange(origins.Count - consumedCount, consumedCount);
                for (var produced = 0; produced < producedCount; produced++)
                {
                    origins.Add([]);
                }
            }

            RejectAll(origins, rejected);
        }

        foreach (var local in rejected)
        {
            promoted.Remove(local);
        }
        if (promoted.Count == 0)
        {
            return AddressTakenScalarPromotionPlan.Empty;
        }

        var calls = synchronizations
            .Select(pair => new
            {
                pair.Key,
                Locals = pair.Value.Locals
                    .Where(promoted.ContainsKey)
                    .Order()
                    .ToImmutableArray(),
                Reloads = pair.Value.Reloads
                    .Where(promoted.ContainsKey)
                    .Order()
                    .ToImmutableArray(),
            })
            .Where(call => !call.Locals.IsEmpty)
            .ToImmutableDictionary(
                call => call.Key,
                call => new AddressTakenScalarCallSynchronization(
                    call.Locals,
                    call.Reloads));

        return new(
            promoted.OrderBy(pair => pair.Key).ToImmutableDictionary(),
            calls);
    }

    private static void ApplyDirectCall(
        AddressTakenScalarPromotionRequest request,
        CilInstruction instruction,
        List<ImmutableHashSet<int>> origins,
        HashSet<int> rejected,
        Dictionary<int, MutableSynchronization> synchronizations)
    {
        var key = new ManagedCallSiteKey(request.Caller, instruction.Offset);
        if (!request.CallSites.TryGetValue(key, out var callSite) ||
            callSite.Operation != ManagedCallOperation.Direct)
        {
            RejectAll(origins, rejected);
            origins.Clear();
            return;
        }

        var target = callSite.Target;
        var hasReceiver = !target.Definition.IsStatic;
        var consumedCount = target.Signature.ParameterTypes.Length +
            (hasReceiver ? 1 : 0);
        if (consumedCount > origins.Count ||
            !request.ByRefEffects.TryGetValue(
                callSite.TargetIdentity.CanonicalName,
                out var effects))
        {
            RejectAll(origins, rejected);
            origins.Clear();
            return;
        }

        var first = origins.Count - consumedCount;
        var synchronization = new MutableSynchronization();
        for (var stackIndex = first; stackIndex < origins.Count; stackIndex++)
        {
            if (origins[stackIndex].IsEmpty)
            {
                continue;
            }

            var relativeIndex = stackIndex - first;
            var argumentIndex = hasReceiver
                ? relativeIndex
                : relativeIndex;
            if (effects.EscapedArguments.Contains(argumentIndex) ||
                effects.ReturnedArgumentAliases.Contains(argumentIndex))
            {
                rejected.UnionWith(origins[stackIndex]);
                continue;
            }

            synchronization.Locals.UnionWith(origins[stackIndex]);
            if (effects.WrittenArguments.Contains(argumentIndex))
            {
                synchronization.Reloads.UnionWith(origins[stackIndex]);
            }
        }

        if (synchronization.Locals.Count != 0)
        {
            if (!synchronizations.TryGetValue(instruction.Offset, out var existing))
            {
                synchronizations.Add(instruction.Offset, synchronization);
            }
            else
            {
                existing.Locals.UnionWith(synchronization.Locals);
                existing.Reloads.UnionWith(synchronization.Reloads);
            }
        }

        origins.RemoveRange(first, consumedCount);
        if (target.Signature.ReturnType != CliValueKind.Void)
        {
            origins.Add([]);
        }
    }

    private static int ProducedCount(CilInstruction instruction) =>
        instruction.Operation switch
        {
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

    private static bool IsPromotable(CliValueKind kind) => kind is
        CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or
        CliValueKind.F8 or CliValueKind.NativeInt;

    private static List<ImmutableHashSet<int>> EmptyOrigins(int count) =>
        Enumerable.Range(0, count)
            .Select(_ => ImmutableHashSet<int>.Empty)
            .ToList();

    private static bool ContainsOrigins(List<ImmutableHashSet<int>> origins) =>
        origins.Any(origin => !origin.IsEmpty);

    private static void RejectAll(
        IEnumerable<ImmutableHashSet<int>> origins,
        HashSet<int> rejected)
    {
        foreach (var origin in origins)
        {
            rejected.UnionWith(origin);
        }
    }

    private sealed class MutableSynchronization
    {
        public HashSet<int> Locals { get; } = [];
        public HashSet<int> Reloads { get; } = [];
    }
}
