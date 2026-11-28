// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync.StateMachineShapes;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

internal interface IAsyncIteratorRewriter
{
    // The runtime async body of an async iterator machine's MoveNextAsync or DisposeAsync (member), from the machine's
    // MoveNext, or null when MoveNext has a shape this does not take (both members then keep their own bodies).
    CilMethodBody? Rewrite(MethodInstanceModel member, CilMethodBody memberBody, MethodInstanceModel moveNextAsync,
        MethodInstanceModel moveNext, CilMethodBody moveNextBody);
}

// Roslyn's async iterator machine is the enumerator: its fields hold what lives across a yield, MoveNextAsync resets the
// promise and runs MoveNext through the builder, and a yield sets the state to a negative value of its own and completes
// the promise with true. Rewritten, the machine stays and MoveNextAsync is `if (state == -2) return false;` and then
// MoveNext's body with each await as StateMachineRewriter takes it (its suspension and resume block gone, so the state
// stays -1 while the frame is suspended), each test of the cached state folded where every state it can hold there gives
// it the same outcome (an await's states it no longer can), promise.SetResult(b) a return of b, builder.Complete() gone,
// and the catch that faulted the promise rethrowing after it sets the state done. DisposeAsync keeps its checks (a
// running or unstarted enumerator throws, a finished one returns), sets dispose mode and awaits MoveNextAsync, whose
// dispose paths run the finally blocks.
// Cost: StateMachineRewriter's linear passes over MoveNext, the machine's states sorted once, and for each read of the
// cached state a lookup in them (FoldStates), so the yields and the protected blocks that test the state do not
// multiply.
internal sealed class AsyncIteratorRewriter(
    Func<IRuntimeAsyncMemberResolver> members,
    IFieldRepository fields,
    ICilBodyLayout layout) : IAsyncIteratorRewriter
{
    private const string State = "<>1__state";
    private const string Builder = "<>t__builder";
    private const string Promise = "<>v__promiseOfValueOrEnd";
    private const string DisposeMode = "<>w__disposeMode";
    private const int Done = -2;

    private readonly Lazy<IRuntimeAsyncMemberResolver> _members = new(members);

    public CilMethodBody? Rewrite(MethodInstanceModel member, CilMethodBody memberBody, MethodInstanceModel moveNextAsync,
        MethodInstanceModel moveNext, CilMethodBody moveNextBody)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(memberBody);
        if (MoveNextAsync(moveNextAsync, WithoutNops(layout, moveNext, moveNextBody)) is not { } rewritten) return null;
        return member.Definition.Key == moveNextAsync.Definition.Key ? rewritten : DisposeAsync(member, memberBody, moveNextAsync);
    }

    private CilMethodBody? MoveNextAsync(MethodInstanceModel moveNextAsync, CilMethodBody moveNext)
    {
        var instructions = moveNext.Instructions;
        if (instructions.Length < 4 || CilStackDepths.Compute(moveNext) is not { } depths ||
            !IsThis(instructions[0]) || !IsField(instructions[1], CilOperation.LoadField, State) ||
            instructions[2] is not { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var stateLocal } })
        {
            return null;
        }
        var stateField = instructions[1].Operand;
        var positions = new Dictionary<int, int>(instructions.Length + 1);
        for (var index = 0; index < instructions.Length; index++) positions[instructions[index].Offset] = index;
        positions[instructions[^1].NextOffset] = instructions.Length;
        if (Producers(moveNext, depths) is not { } producers) return null;
        var rewrite = new Rewriting(instructions.Length);

        // Each await, by its builder call.
        var temporaries = Temporaries(moveNext);
        for (var index = 0; index < instructions.Length; index++)
        {
            if (rewrite.Removed[index] ||
                !(IsBuilderCall(instructions[index], "AwaitUnsafeOnCompleted") || IsBuilderCall(instructions[index], "AwaitOnCompleted")))
            {
                continue;
            }
            if (!Await(_members.Value, moveNext, index, stateLocal, positions, temporaries, rewrite)) return null;
        }

        // builder.Complete() goes; promise.SetResult(b); ret returns b; promise.SetException(e) rethrows.
        var exits = new HashSet<int>();
        var faults = 0;
        for (var index = 0; index < instructions.Length; index++)
        {
            if (rewrite.Removed[index] ||
                instructions[index] is not { Operation: CilOperation.Call, Operand: CilOperand.MethodInstance { Value: var method } })
            {
                continue;
            }
            var address = producers[index];
            var name = method.Definition.Name;
            if (name == "Complete" && AsyncStateMachineKickoffs.TakesIteratorBuilder(method.DeclaringType))
            {
                if (!OnThisField(instructions, producers, address, Builder)) return null;
                Remove(rewrite, producers[address], 1);
                Remove(rewrite, address, 1);
                Remove(rewrite, index, 1);
                continue;
            }
            if (name is not ("SetResult" or "SetException") || !OnThisField(instructions, producers, address, Promise)) continue;
            if (name == "SetResult")
            {
                if (index + 1 >= instructions.Length || instructions[index + 1].Operation != CilOperation.Return) return null;
                exits.Add(index + 1);
            }
            else
            {
                if (index == 0 || instructions[index - 1].Operation != CilOperation.LoadLocal) return null;
                Remove(rewrite, index - 1, 1);
                rewrite.Replaced[index] = [Op(CilOperation.Rethrow)];
                faults++;
            }
            Remove(rewrite, producers[address], 1);
            Remove(rewrite, address, 1);
            if (name == "SetResult") Remove(rewrite, index, 1);
        }
        if (exits.Count == 0 || faults != 1) return null;

        // The cached state: each test folded where all its possible values agree. What it can hold at a read: the
        // machine's negative states (before the start, running, done and after each yield, each stored by MoveNext or
        // its constructor and GetAsyncEnumerator); an await's states are gone with its suspension.
        var states = new SortedSet<int> { -1, Done, -3 };
        for (var index = 0; index + 1 < instructions.Length; index++)
        {
            if (instructions[index] is { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: < 0 and var stored } } &&
                instructions[index + 1].Operation == CilOperation.Duplicate &&
                index + 3 < instructions.Length && IsField(instructions[index + 3], CilOperation.StoreField, State))
            {
                states.Add(stored);
            }
        }
        var possible = new StateSet(states);
        for (var index = 3; index < instructions.Length; index++)
        {
            if (rewrite.Removed[index] ||
                instructions[index] is not { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } } ||
                local != stateLocal)
            {
                continue;
            }
            if (FoldStates(instructions, index, positions, rewrite, possible) == StateFold.Unmatched) return null;
        }

        return Lay(moveNextAsync, moveNext, stateField, rewrite, exits, positions);
    }

    // MoveNextAsync's prologue, then MoveNext's elements as rewritten, its regions kept. A return that is not one of the
    // exits (MoveNext's own, after a suspension it no longer has) must be unreached.
    private CilMethodBody? Lay(MethodInstanceModel moveNextAsync, CilMethodBody moveNext, CilOperand stateField,
        Rewriting rewrite, HashSet<int> exits, Dictionary<int, int> positions)
    {
        var reading = CilBodyDrafts.Read(moveNext);
        var labels = reading.Labels;
        var offsets = new Dictionary<CilBodyLabel, int>(ReferenceEqualityComparer.Instance);
        foreach (var (offset, label) in labels) offsets[label] = offset;
        var locals = reading.Draft.Locals;
        var running = new CilBodyLabel();
        var elements = new List<CilBodyElement>(reading.Draft.Elements.Length + 8)
        {
            Argument(0),
            Op(CilOperation.LoadField, stateField),
            Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(Done)),
            new CilBodyElement.Branch(CilOperation.BranchIfNotEqual, running),
            Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            Op(CilOperation.Return),
            new CilBodyElement.Mark(running),
        };
        var returns = new HashSet<CilBodyElement>(ReferenceEqualityComparer.Instance);
        var index = -1;
        foreach (var element in reading.Draft.Elements)
        {
            if (element is CilBodyElement.Mark mark)
            {
                index = positions[offsets[mark.Label]];
                elements.Add(mark);
                continue;
            }
            if (rewrite.Replaced.TryGetValue(index, out var replacement))
            {
                elements.AddRange(replacement.Select(item => Labeled(item, locals, labels) with { Source = element.Source }));
                continue;
            }
            if (rewrite.Removed[index]) continue;
            if (element is CilBodyElement.Instruction { Operation: CilOperation.Return } && !exits.Contains(index)) returns.Add(element);
            elements.Add(element);
        }
        var regions = reading.Draft.Regions;
        var reached = Reachable(elements, regions).ToImmutableArray();
        foreach (var element in reached)
        {
            if (returns.Contains(element)) return null;
            var named = element switch
            {
                CilBodyElement.Branch branch => [branch.Target],
                CilBodyElement.Switch table => table.Targets,
                _ => ImmutableArray<CilBodyLabel>.Empty,
            };
            foreach (var label in named)
            {
                if (!offsets.TryGetValue(label, out var offset)) continue;
                var position = positions[offset];
                if (position < rewrite.Blocked.Length && rewrite.Blocked[position]) return null;
            }
        }
        var draft = new CilBodyDraft(locals, reached, regions) { InitializesLocals = true };
        return layout.Lay(draft, moveNextAsync, moveNext.MaxStack + 2);
    }

    // DisposeAsync: a running or unstarted enumerator throws (Roslyn's NotSupportedException), a finished one returns,
    // and otherwise dispose mode is set and MoveNextAsync awaited. The fields and the exception come from Roslyn's own
    // DisposeAsync, so one it wrote otherwise keeps its body.
    private CilMethodBody? DisposeAsync(MethodInstanceModel disposeAsync, CilMethodBody body, MethodInstanceModel moveNextAsync)
    {
        var instructions = body.Instructions;
        var state = instructions.FirstOrDefault(instruction => IsField(instruction, CilOperation.LoadField, State))?.Operand;
        var mode = instructions.FirstOrDefault(instruction => IsField(instruction, CilOperation.StoreField, DisposeMode))?.Operand;
        var unsupported = instructions.FirstOrDefault(instruction => instruction is
        {
            Operation: CilOperation.NewObject,
            Operand: CilOperand.MethodInstance { Value.DeclaringType.FullName: "System.NotSupportedException" },
        })?.Operand;
        if (state is null || mode is null || unsupported is null) return null;
        var await = _members.Value.Resolve(new RuntimeAsyncMember.AsyncHelpersForAwaited("Await",
            moveNextAsync.Signature.ReturnSignatureType));
        var started = new CilBodyLabel();
        var running = new CilBodyLabel();
        ImmutableArray<CilBodyElement> elements =
        [
            Argument(0),
            Op(CilOperation.LoadField, state),
            Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(-1)),
            new CilBodyElement.Branch(CilOperation.BranchIfLessThanSigned, started),
            Op(CilOperation.NewObject, unsupported),
            Op(CilOperation.Throw),
            new CilBodyElement.Mark(started),
            Argument(0),
            Op(CilOperation.LoadField, state),
            Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(Done)),
            new CilBodyElement.Branch(CilOperation.BranchIfNotEqual, running),
            Op(CilOperation.Return),
            new CilBodyElement.Mark(running),
            Argument(0),
            Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            Op(CilOperation.StoreField, mode),
            Argument(0),
            Call(moveNextAsync),
            Call(await),
            Op(CilOperation.Pop),
            Op(CilOperation.Return),
        ];
        return layout.Lay(new CilBodyDraft([], elements, []) { InitializesLocals = true }, disposeAsync, 2);
    }

    // Whether an address is `ldarg.0; ldflda <field>` of the machine's field of that name.
    private bool OnThisField(ImmutableArray<CilInstruction> instructions, int[] producers, int address, string field) =>
        address >= 0 && IsField(instructions[address], CilOperation.LoadFieldAddress, field) &&
        producers[address] >= 0 && IsThis(instructions[producers[address]]);

    // Whether an instruction is the field operation on a field of the name: a generic machine's field is an instance,
    // any other's its definition's key.
    private bool IsField(CilInstruction instruction, CilOperation operation, string name) =>
        instruction.Operation == operation && instruction.Operand switch
        {
            CilOperand.FieldInstance { Value: var field } => field.Definition.Name == name,
            CilOperand.Entity { Key: var key } => fields.GetField(key).Name == name,
            _ => false,
        };
}
