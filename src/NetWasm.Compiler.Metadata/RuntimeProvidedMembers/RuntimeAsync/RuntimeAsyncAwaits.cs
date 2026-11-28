// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// The awaits of a runtime-async method's CIL (RuntimeAsyncSourceReader's), each as the lowering takes it: of a statically
// bound call of a lowered method, which the caller calls directly; of a virtual or interface call, whose override may
// take it directly; or of an awaitable or awaiter, a leaf. RuntimeAsyncBodyBuilder lowers them, and the suspension
// facts read what each method awaits from the same classification.
internal static class RuntimeAsyncAwaits
{
    // An await (the AsyncHelpers call at Index) and how the lowering takes it.
    internal sealed record Await(int Index, MethodInstanceModel Helper, CliTypeIdentity Awaited, bool IsAwaiter,
        int? DirectCallOffset, int? VirtualCallOffset, ImmutableArray<int> Dropped, bool ContinuesAnywhere,
        CliTypeIdentity? FusedTask);

    internal enum ResultShape
    {
        Task,
        TaskOfResult,
        ValueTask,
        ValueTaskOfResult,
    }

    // Each await of the body: a call of the AsyncHelpers type. One pass over the instructions, after one each for the
    // branch targets and the ConfigureAwait locals.
    internal static ImmutableArray<Await> Classify(CilMethodBody body, EntityKey helpers)
    {
        ArgumentNullException.ThrowIfNull(body);
        var awaits = ImmutableArray.CreateBuilder<Await>();
        var targets = BranchTargets(body);
        // The locals only ConfigureAwait patterns use: a ValueTask's goes through a local, which Roslyn reuses across
        // statements, so a fused pattern may drop its store only when nothing else reads the local.
        var configuringLocals = ConfiguringLocals(body);
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var instruction = body.Instructions[index];
            if (instruction is not { Operation: CilOperation.Call, Operand: CilOperand.MethodInstance { Value: var callee } } ||
                callee.Definition.DeclaringType != helpers)
            {
                continue;
            }
            var isAwaiter = callee.Definition.Name is "AwaitAwaiter" or "UnsafeAwaitAwaiter";
            var awaited = isAwaiter ? callee.MethodArguments[0] : callee.Signature.ParameterSignatureTypes[0];
            var configured = isAwaiter
                ? null
                : ConfiguredCall(body.Instructions, index, targets, configuringLocals,
                    (call, task) => DirectCall(body.Instructions[call], task));
            var direct = configured?.CallOffset ?? (isAwaiter || index == 0 || targets.Contains(instruction.Offset)
                ? null
                : DirectCall(body.Instructions[index - 1], awaited));
            var configuredVirtual = isAwaiter || direct is not null
                ? null
                : ConfiguredCall(body.Instructions, index, targets, configuringLocals,
                    (call, task) => VirtualCall(body.Instructions, call, task));
            var virtualCall = isAwaiter || direct is not null
                ? null
                : configuredVirtual?.CallOffset ?? (index == 0 || targets.Contains(instruction.Offset)
                    ? null
                    : VirtualCall(body.Instructions, index - 1, awaited));
            // Any other task or value task awaited through ConfigureAwait with a constant (a leaf await): the await
            // takes the task itself, as a direct call's await does, and drops what configured it. The configured
            // awaitable nests the task in two structs, which stay whole in memory where the task alone splits.
            var configuredLeaf = isAwaiter || direct is not null || virtualCall is not null
                ? null
                : ConfiguredCall(body.Instructions, index, targets, configuringLocals,
                    (call, _) => body.Instructions[call].Offset);
            var fused = configured ?? configuredVirtual ?? configuredLeaf;
            awaits.Add(new(index, callee, awaited, isAwaiter, direct, virtualCall, fused?.Dropped ?? [],
                fused?.ContinuesAnywhere ?? false, fused?.Task));
        }
        return awaits.ToImmutable();
    }

    internal static (ResultShape Shape, CliTypeIdentity? Result)? Shape(CliTypeIdentity returnType)
    {
        var name = returnType.Shape == CliTypeShape.GenericInstantiation ? returnType.ElementType!.FullName : returnType.FullName;
        return name switch
        {
            "System.Threading.Tasks.Task" => (ResultShape.Task, null),
            "System.Threading.Tasks.Task`1" => (ResultShape.TaskOfResult, returnType.TypeArguments[0]),
            "System.Threading.Tasks.ValueTask" => (ResultShape.ValueTask, null),
            "System.Threading.Tasks.ValueTask`1" => (ResultShape.ValueTaskOfResult, returnType.TypeArguments[0]),
            _ => null,
        };
    }

    // The offset of a call the await can take directly: a statically bound call of a runtime-async method whose task
    // the await takes at once.
    internal static int? DirectCall(CilInstruction previous, CliTypeIdentity awaited) =>
        previous is { Operand: CilOperand.MethodInstance { Value: var target } } &&
        (previous.Operation == CilOperation.Call ||
            previous.Operation == CilOperation.CallVirtual && !target.Definition.IsVirtual) &&
        target.Definition.IsRuntimeAsync && target.Signature.ReturnSignatureType.Equals(awaited)
            ? previous.Offset
            : null;

    // An await of a call's Task or ValueTask through ConfigureAwait with a constant, as Roslyn writes it:
    // `call M; ldc.i4 b; callvirt Task.ConfigureAwait(bool); call Await(configured)`, with nothing branching to what
    // follows the call, when `call` accepts M's call (DirectCall, VirtualCall), or any instruction that leaves the task
    // for a leaf await. The call is taken directly, and what configured its task is dropped.
    internal static (int CallOffset, ImmutableArray<int> Dropped, bool ContinuesAnywhere, CliTypeIdentity Task)?
        ConfiguredCall(ImmutableArray<CilInstruction> instructions, int index, HashSet<int> targets, bool[] configuringLocals,
            Func<int, CliTypeIdentity, int?> call)
    {
        if (index < 3) return null;
        var configure = instructions[index - 1];
        var constant = instructions[index - 2];
        if (targets.Contains(instructions[index].Offset) || targets.Contains(configure.Offset) ||
            targets.Contains(constant.Offset) ||
            configure is not
            {
                Operation: CilOperation.Call or CilOperation.CallVirtual,
                Operand: CilOperand.MethodInstance { Value: { Definition.Name: "ConfigureAwait" } method },
            } ||
            method.Signature.ParameterSignatureTypes is not [{ CanonicalName: "primitive:bool" }] ||
            constant is not { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: 0 or 1 } flag })
        {
            return null;
        }
        switch (Shape(method.DeclaringType)?.Shape)
        {
            case ResultShape.Task or ResultShape.TaskOfResult:
                return call(index - 3, method.DeclaringType) is { } callOffset
                    ? (callOffset, [constant.Offset, configure.Offset], flag.Value == 0, method.DeclaringType)
                    : null;
            // A ValueTask is a struct: Roslyn stores it in a local of its own to call ConfigureAwait on its address,
            // `call M; stloc v; ldloca v; ldc.i4 b; call ValueTask.ConfigureAwait(bool)`.
            case ResultShape.ValueTask or ResultShape.ValueTaskOfResult:
                if (index < 5) return null;
                var address = instructions[index - 3];
                var store = instructions[index - 4];
                return targets.Contains(address.Offset) || targets.Contains(store.Offset) ||
                    address is not { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var local } } ||
                    store is not { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var stored } } ||
                    stored != local || (uint)local >= (uint)configuringLocals.Length || !configuringLocals[local] ||
                    call(index - 5, method.DeclaringType) is not { } valueCallOffset
                    ? null
                    : (valueCallOffset, [store.Offset, address.Offset, constant.Offset, configure.Offset], flag.Value == 0,
                        method.DeclaringType);
            default:
                return null;
        }
    }

    // Per local, whether every instruction that names it is the store or the address of a ValueTask ConfigureAwait
    // pattern (`stloc v; ldloca v; ldc.i4 b; call ValueTask.ConfigureAwait(bool)`): one pass to count each local's
    // uses, one to count its pattern uses.
    internal static bool[] ConfiguringLocals(CilMethodBody body)
    {
        var instructions = body.Instructions;
        var uses = new int[body.Locals.Length];
        var patternUses = new int[body.Locals.Length];
        for (var index = 0; index < instructions.Length; index++)
        {
            if (instructions[index] is not
                {
                    Operation: CilOperation.LoadLocal or CilOperation.StoreLocal or CilOperation.LoadLocalAddress,
                    Operand: CilOperand.Index { Value: var local },
                } || (uint)local >= (uint)uses.Length)
            {
                continue;
            }
            uses[local]++;
            if (instructions[index].Operation == CilOperation.StoreLocal && index + 3 < instructions.Length &&
                instructions[index + 1] is { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var address } } &&
                address == local &&
                instructions[index + 2].Operation == CilOperation.LoadInt32 &&
                instructions[index + 3] is
                {
                    Operation: CilOperation.Call or CilOperation.CallVirtual,
                    Operand: CilOperand.MethodInstance { Value.Definition.Name: "ConfigureAwait" },
                })
            {
                patternUses[local] += 2;
            }
        }
        var configuring = new bool[uses.Length];
        for (var local = 0; local < uses.Length; local++) configuring[local] = uses[local] > 0 && uses[local] == patternUses[local];
        return configuring;
    }

    // The offset of a virtual or interface call whose task the await takes at once: a callvirt of a virtual method
    // returning a Task or a ValueTask (not through a constrained. prefix, whose receiver is an address).
    internal static int? VirtualCall(ImmutableArray<CilInstruction> instructions, int index, CliTypeIdentity awaited)
    {
        var previous = instructions[index];
        if (previous is not
            {
                Operation: CilOperation.CallVirtual,
                Operand: CilOperand.MethodInstance { Value: { Definition.IsVirtual: true } target },
            } ||
            !target.Signature.ReturnSignatureType.Equals(awaited) || Shape(awaited) is null)
        {
            return null;
        }
        return index > 0 && instructions[index - 1].Operation == CilOperation.Constrained ? null : previous.Offset;
    }

    // Every offset a branch or a switch can reach, and every protected region's boundaries: control can arrive there
    // other than by falling through.
    internal static HashSet<int> BranchTargets(CilMethodBody body)
    {
        var targets = new HashSet<int>();
        foreach (var instruction in body.Instructions)
        {
            if (instruction.Operand is CilOperand.BranchTarget branch) targets.Add(branch.Offset);
            else if (instruction.Operand is CilOperand.SwitchTargets table) targets.UnionWith(table.Offsets);
        }
        foreach (var region in body.ExceptionRegions)
        {
            targets.Add(region.TryOffset);
            targets.Add(region.HandlerOffset);
            targets.Add(region.TryOffset + region.TryLength);
            targets.Add(region.HandlerOffset + region.HandlerLength);
            if (region.FilterOffset is { } filter) targets.Add(filter);
        }
        return targets;
    }
}
