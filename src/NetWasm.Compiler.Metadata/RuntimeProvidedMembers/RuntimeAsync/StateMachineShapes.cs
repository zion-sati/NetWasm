// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// The shapes Roslyn's async state machines share, matched for the bodies rewritten from them (StateMachineRewriter, a
// kickoff's; an async iterator's MoveNextAsync): an await at its builder call, a read of the cached state and the test
// consuming it, each instruction's operand producers, the temporaries a struct awaitable goes through, MoveNext without
// its nops, a body laid out without a removed catch, and the kickoff awaiting its task when a shape does not match.
// Each pass is linear in MoveNext's instructions.
internal static class StateMachineShapes
{
    internal const string Namespace = "System.Runtime.CompilerServices.";
    internal const int SuspensionReach = 24;

    // The awaitable types whose awaiter the rewrite turns back into an Await of the awaitable: by the definition that
    // declares GetAwaiter, and whether it is a struct (Roslyn then calls GetAwaiter on a temporary's address).
    internal static readonly Dictionary<string, bool> Awaitables = new(StringComparer.Ordinal)
    {
        ["System.Threading.Tasks.Task"] = false,
        ["System.Threading.Tasks.Task`1"] = false,
        ["System.Threading.Tasks.ValueTask"] = true,
        ["System.Threading.Tasks.ValueTask`1"] = true,
        [Namespace + "ConfiguredTaskAwaitable"] = true,
        [Namespace + "ConfiguredTaskAwaitable`1"] = true,
        [Namespace + "ConfiguredValueTaskAwaitable"] = true,
        [Namespace + "ConfiguredValueTaskAwaitable`1"] = true,
    };

    // The kickoff as Roslyn wrote it, awaiting the task it returns without its caller's context, as the caller would
    // await the task itself: `task.ConfigureAwait(false)` (a value task through a temporary for its address).
    internal static CilMethodBody Awaiting(IRuntimeAsyncMemberResolver members, ICilBodyLayout layout,
        MethodInstanceModel kickoff, CilMethodBody kickoffBody)
    {
        var reading = CilBodyDrafts.Read(kickoffBody);
        var task = kickoff.Signature.ReturnSignatureType;
        var configure = members.Resolve(new RuntimeAsyncMember.ConfigureAwait(task));
        var await = Call(members.Resolve(new RuntimeAsyncMember.AsyncHelpersForAwaited("Await",
            configure.Signature.ReturnSignatureType)));
        var temporary = task.IsValueType ? new CilBodyLocal(task) : null;
        var elements = new List<CilBodyElement>(reading.Draft.Elements.Length + 5);
        foreach (var element in reading.Draft.Elements)
        {
            if (element is CilBodyElement.Instruction { Operation: CilOperation.Return })
            {
                if (temporary is not null)
                {
                    elements.Add(Store(temporary));
                    elements.Add(Address(temporary));
                }
                elements.Add(Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)));
                elements.Add(Op(temporary is null ? CilOperation.CallVirtual : CilOperation.Call,
                    new CilOperand.MethodInstance(configure)));
                elements.Add(await with { Source = element.Source });
            }
            elements.Add(element);
        }
        return layout.Lay(reading.Draft with
        {
            Locals = temporary is null ? reading.Draft.Locals : [.. reading.Draft.Locals, temporary],
            Elements = [.. elements],
        }, kickoff, kickoffBody.MaxStack + 2);
    }

    // MoveNext without the nops that would split the shapes matched below: Debug IL has them for its sequence points and
    // pads each access to a local past the 255th with two (`stloc 255; nop; nop; ldloca 255; nop; nop`), and the
    // rewritten body keeps no source offsets for them to carry. A branch to one goes to the instruction after it. A nop
    // before a protected block's boundary stays: the decoder enters a try that IL branches into through a nop just before
    // it, which must stay outside.
    internal static CilMethodBody WithoutNops(ICilBodyLayout layout, MethodInstanceModel method, CilMethodBody body)
    {
        var instructions = body.Instructions;
        var boundaries = new HashSet<int>();
        foreach (var region in body.ExceptionRegions)
        {
            boundaries.Add(region.TryOffset);
            boundaries.Add(region.TryOffset + region.TryLength);
            boundaries.Add(region.HandlerOffset);
            boundaries.Add(region.HandlerOffset + region.HandlerLength);
            if (region.FilterOffset is { } filter) boundaries.Add(filter);
        }
        var removed = new HashSet<int>();
        for (var index = 0; index < instructions.Length; index++)
        {
            if (instructions[index].Operation == CilOperation.Nop && !boundaries.Contains(instructions[index].NextOffset))
                removed.Add(instructions[index].Offset);
        }
        if (removed.Count == 0) return body;
        var reading = CilBodyDrafts.Read(body);
        var offsets = new Dictionary<CilBodyLabel, int>(ReferenceEqualityComparer.Instance);
        foreach (var (offset, label) in reading.Labels) offsets[label] = offset;
        var elements = new List<CilBodyElement>(reading.Draft.Elements.Length);
        var at = -1;
        foreach (var element in reading.Draft.Elements)
        {
            if (element is CilBodyElement.Mark mark) at = offsets[mark.Label];
            else if (element is CilBodyElement.Instruction { Operation: CilOperation.Nop } && removed.Contains(at)) continue;
            elements.Add(element);
        }
        return layout.Lay(reading.Draft with { Elements = [.. elements] }, method, body.MaxStack);
    }

    // One await at its builder call: the IsCompleted test before the suspension, the suspension (from its state store to
    // its leave), and the resumption up to the GetResult the test branches to (after the machine's address, when the
    // result goes to one of its fields). Nothing kept may branch into what goes.
    internal static bool Await(IRuntimeAsyncMemberResolver members, CilMethodBody moveNext, int call, int stateLocal,
        Dictionary<int, int> positions, bool[] temporaries, Rewriting rewrite)
    {
        var instructions = moveNext.Instructions;
        var start = call - 4;
        while (start >= 0 && call - start <= SuspensionReach && !(IsThis(instructions[start]) &&
            instructions[start + 1].Operation == CilOperation.LoadInt32 &&
            instructions[start + 2].Operation == CilOperation.Duplicate &&
            instructions[start + 3] is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var local } } &&
            local == stateLocal))
        {
            start--;
        }
        var leave = call + 1;
        if (start < 3 || call - start > SuspensionReach || leave >= instructions.Length ||
            instructions[leave].Operation != CilOperation.Leave ||
            instructions[start - 1] is not { Operation: CilOperation.BranchIfTrue, Operand: CilOperand.BranchTarget { Offset: var completed } } ||
            instructions[start - 2] is not { Operation: CilOperation.Call, Operand: CilOperand.MethodInstance { Value.Definition.Name: "get_IsCompleted" } } ||
            instructions[start - 3] is not { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var awaiter } } ||
            !positions.TryGetValue(completed, out var resumeEnd) || resumeEnd <= leave)
        {
            return false;
        }
        var getResult = resumeEnd + 1 < instructions.Length && IsThis(instructions[resumeEnd]) ? resumeEnd + 1 : resumeEnd;
        if (getResult + 1 >= instructions.Length ||
            instructions[getResult] is not { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var read } } ||
            read != awaiter ||
            instructions[getResult + 1] is not { Operation: CilOperation.Call, Operand: CilOperand.MethodInstance { Value.Definition.Name: "GetResult" } })
        {
            return false;
        }

        // An awaitable this knows: its Await, from the GetAwaiter (or the temporary a struct goes through) on to the
        // GetResult, whose value is the Await's.
        if (start >= 5 &&
            instructions[start - 4] is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var stored } } &&
            stored == awaiter &&
            instructions[start - 5] is
            {
                Operation: CilOperation.Call or CilOperation.CallVirtual,
                Operand: CilOperand.MethodInstance { Value: { Definition.Name: "GetAwaiter" } getAwaiter },
            } &&
            DefinitionName(getAwaiter.DeclaringType) is { } awaitable &&
            Awaitables.TryGetValue(awaitable, out var isStruct))
        {
            var first = start - 5;
            if (isStruct)
            {
                // `stloc v; ldloca v; call GetAwaiter`, v only ever stored and its address taken right after.
                first = first >= 2 &&
                    instructions[first - 1] is { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var temporary } } &&
                    instructions[first - 2] is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var spilled } } &&
                    spilled == temporary && temporaries[temporary]
                        ? first - 2
                        : -1;
            }
            if (first >= 0)
            {
                // A field's machine address before GetResult stays for the field access to take.
                Drop(rewrite, first + 1, resumeEnd - (first + 1));
                Array.Fill(rewrite.Blocked, true, resumeEnd, getResult - resumeEnd);
                Drop(rewrite, getResult, 2);
                rewrite.Replaced[first] = [Call(members.Resolve(new RuntimeAsyncMember.AsyncHelpersForAwaited("Await",
                    getAwaiter.DeclaringType)))];
                return true;
            }
        }

        // Any other awaiter: its own await after the IsCompleted test, then GetResult.
        Drop(rewrite, start, resumeEnd - start);
        rewrite.Replaced[start] =
        [
            Op(CilOperation.LoadLocal, new CilOperand.Index(awaiter)),
            Call(members.Resolve(new RuntimeAsyncMember.AsyncHelpersNamed(
                IsBuilderCall(instructions[call], "AwaitUnsafeOnCompleted") ? "UnsafeAwaitAwaiter" : "AwaitAwaiter",
                [moveNext.LocalSignatureTypes[awaiter]]))),
        ];
        return true;
    }

    // A read of the cached state, which is -1 now: the test that consumes it, after any `ldc.i4 k; sub` that starts a
    // dispatch's states at k (`ldloc state; ldc.i4.1; sub; ldc.i4.1; ble.un` for states 1 and 2), folds to a branch or to
    // nothing.
    internal static bool FoldState(ImmutableArray<CilInstruction> instructions, int index, Dictionary<int, int> positions,
        Rewriting rewrite)
    {
        bool Present(int at) => at < instructions.Length && !rewrite.Removed[at];
        if (!Present(index + 1)) return false;
        if (FoldSwitch(instructions, index, rewrite) is { } folded) return folded;
        var value = -1;
        var at = index + 1;
        if (Present(at + 1) &&
            instructions[at] is { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: var start } } &&
            instructions[at + 1].Operation == CilOperation.Subtract)
        {
            value = unchecked(-1 - start);
            at += 2;
            if (!Present(at)) return false;
        }
        var next = instructions[at];
        bool? taken = null;
        var end = at + 1;
        var target = next.Operand;
        switch (next.Operation)
        {
            case CilOperation.BranchIfFalse:
                taken = value == 0;
                break;
            case CilOperation.BranchIfTrue:
                taken = value != 0;
                break;
            case CilOperation.Pop:
                taken = false;
                break;
            case CilOperation.Switch when next.Operand is CilOperand.SwitchTargets table:
                taken = (uint)value < (uint)table.Offsets.Length;
                if (taken == true) target = new CilOperand.BranchTarget(table.Offsets[value]);
                break;
            case CilOperation.LoadInt32 when next.Operand is CilOperand.ConstantI4 { Value: var constant } && Present(at + 1):
                var test = instructions[at + 1];
                end = at + 2;
                target = test.Operand;
                taken = test.Operation switch
                {
                    // `ldc.i4 k; pop; pop`: a test the decoder emptied (a branch to the next instruction).
                    CilOperation.Pop when Present(at + 2) && instructions[at + 2].Operation == CilOperation.Pop => Emptied(),
                    CilOperation.BranchIfEqual => value == constant,
                    CilOperation.BranchIfNotEqual => value != constant,
                    CilOperation.BranchIfGreaterThanSigned => value > constant,
                    CilOperation.BranchIfGreaterThanOrEqualSigned => value >= constant,
                    CilOperation.BranchIfLessThanSigned => value < constant,
                    CilOperation.BranchIfLessThanOrEqualSigned => value <= constant,
                    CilOperation.BranchIfGreaterThanUnsigned => (uint)value > (uint)constant,
                    CilOperation.BranchIfGreaterThanOrEqualUnsigned => (uint)value >= (uint)constant,
                    CilOperation.BranchIfLessThanUnsigned => (uint)value < (uint)constant,
                    CilOperation.BranchIfLessThanOrEqualUnsigned => (uint)value <= (uint)constant,
                    _ => null,
                };
                break;
        }
        if (taken is null) return false;
        Remove(rewrite, index, 1);
        Drop(rewrite, index + 1, end - (index + 1));
        if (taken == true)
        {
            if (target is not CilOperand.BranchTarget branch || !positions.ContainsKey(branch.Offset)) return false;
            rewrite.Replaced[index] = [Op(CilOperation.Branch, branch)];
        }
        return true;

        bool Emptied()
        {
            end = at + 3;
            return false;
        }
    }

    // The decoder writes a switch as a chain (CilSwitchLowerer): `dup; ldc.i4 c; bne.un next; pop; br case` for each
    // case, then `pop`. On the state (less a constant, `ldc.i4 k; sub`, when the cases start at k), -1 takes the case it
    // equals or none; the chain goes either way, a branch to that case in its place. Null when no chain follows.
    internal static bool? FoldSwitch(ImmutableArray<CilInstruction> instructions, int index, Rewriting rewrite)
    {
        bool Present(int at) => at < instructions.Length && !rewrite.Removed[at];
        var value = -1;
        var at = index + 1;
        if (Present(at + 1) && instructions[at] is { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: var start } } &&
            instructions[at + 1].Operation == CilOperation.Subtract && Present(at + 2) &&
            instructions[at + 2].Operation == CilOperation.Duplicate)
        {
            value = unchecked(-1 - start);
            at += 2;
        }
        if (!Present(at) || instructions[at].Operation != CilOperation.Duplicate) return null;
        CilOperand? taken = null;
        while (Present(at + 4) && instructions[at].Operation == CilOperation.Duplicate)
        {
            if (instructions[at + 1] is not { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: var constant } } ||
                instructions[at + 2] is not { Operation: CilOperation.BranchIfNotEqual, Operand: CilOperand.BranchTarget { Offset: var next } } ||
                next != instructions[at + 5 < instructions.Length ? at + 5 : at].Offset ||
                instructions[at + 3].Operation != CilOperation.Pop ||
                instructions[at + 4] is not { Operation: CilOperation.Branch, Operand: var target })
            {
                return false;
            }
            if (taken is null && constant == value) taken = target;
            at += 5;
        }
        if (!Present(at) || instructions[at].Operation != CilOperation.Pop) return false;
        Remove(rewrite, index, 1);
        Drop(rewrite, index + 1, at - index);
        if (taken is not null) rewrite.Replaced[index] = [Op(CilOperation.Branch, taken)];
        return true;
    }

    // A read of the cached state when it can hold any of `states` (an async iterator's: before the start, running,
    // done and after each yield, once its awaits no longer suspend the machine), after any `ldc.i4 k; sub` that starts a
    // dispatch's states at k. The test that consumes it folds as FoldState does when every state gives it the same
    // outcome, and stays otherwise; of a switch chain, the cases no state takes go. Unmatched: a consumer this does not
    // know. Cost: a membership or bound lookup in `states` per read, and a switch's or chain's own cases.
    internal static StateFold FoldStates(ImmutableArray<CilInstruction> instructions, int index,
        Dictionary<int, int> positions, Rewriting rewrite, StateSet states)
    {
        bool Present(int at) => at < instructions.Length && !rewrite.Removed[at];
        if (!Present(index + 1)) return StateFold.Unmatched;
        var start = 0;
        var at = index + 1;
        if (Present(at + 1) &&
            instructions[at] is { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: var shift } } &&
            instructions[at + 1].Operation == CilOperation.Subtract)
        {
            start = shift;
            at += 2;
            if (!Present(at)) return StateFold.Unmatched;
        }
        if (instructions[at].Operation == CilOperation.Duplicate) return FoldChain(instructions, index, at, rewrite, states, start);

        var next = instructions[at];
        var end = at + 1;
        Outcome outcome;
        switch (next.Operation)
        {
            case CilOperation.BranchIfFalse:
                outcome = Equal(states, start, next.Operand, taken: true);
                break;
            case CilOperation.BranchIfTrue:
                outcome = Equal(states, start, next.Operand, taken: false);
                break;
            case CilOperation.Pop:
                outcome = Outcome.Never;
                break;
            case CilOperation.Switch when next.Operand is CilOperand.SwitchTargets table:
                outcome = Table(states, start, table);
                break;
            case CilOperation.LoadInt32 when next.Operand is CilOperand.ConstantI4 { Value: var constant } && Present(at + 1):
                if (Compare(instructions, at + 1, states, start, constant, Present) is not { } compared) return StateFold.Unmatched;
                outcome = compared;
                end = instructions[at + 1].Operation == CilOperation.Pop ? at + 3 : at + 2;
                break;
            default:
                return StateFold.Unmatched;
        }
        if (outcome.Mixed) return StateFold.Kept;
        Remove(rewrite, index, 1);
        Drop(rewrite, index + 1, end - (index + 1));
        if (outcome.Taken)
        {
            if (outcome.Target is not CilOperand.BranchTarget branch || !positions.ContainsKey(branch.Offset))
                return StateFold.Unmatched;
            rewrite.Replaced[index] = [Op(CilOperation.Branch, branch)];
        }
        return StateFold.Folded;
    }

    // A test of the shifted state against a constant's equality (`brfalse` is equality with zero): taken (as `taken`
    // says) for the one state that equals it, the other way for the rest.
    private static Outcome Equal(StateSet states, int start, CilOperand target, bool taken, int constant = 0)
    {
        var equal = states.Contains(unchecked(constant + start));
        if (equal && states.Count > 1) return Outcome.Either;
        var result = equal == taken;
        return result ? Outcome.Always(target) : Outcome.Never;
    }

    // A switch on the shifted state: a state below its table's length takes that case, the rest fall through. Only the
    // table's entries are looked up.
    private static Outcome Table(StateSet states, int start, CilOperand.SwitchTargets table)
    {
        var matched = 0;
        int? target = null;
        for (var position = 0; position < table.Offsets.Length; position++)
        {
            if (!states.Contains(unchecked(position + start))) continue;
            matched++;
            if (target is null) target = table.Offsets[position];
            else if (target != table.Offsets[position]) return Outcome.Either;
        }
        if (matched == 0) return Outcome.Never;
        return matched == states.Count ? Outcome.Always(new CilOperand.BranchTarget(target!.Value)) : Outcome.Either;
    }

    // A comparison of the shifted state with a constant: the same outcome for every state, or not; for the decoder's
    // emptied test (`ldc.i4 k; pop; pop`), never taken; null for any other consumer. An ordered comparison is monotonic
    // in the state, so the states' least and greatest values (in the comparison's order) decide it; where the shift
    // could wrap, it is kept.
    private static Outcome? Compare(ImmutableArray<CilInstruction> instructions, int at, StateSet states, int start,
        int constant, Func<int, bool> present)
    {
        var test = instructions[at];
        Func<long, long, bool>? ordered = test.Operation switch
        {
            CilOperation.BranchIfGreaterThanSigned or CilOperation.BranchIfGreaterThanUnsigned => (value, bound) => value > bound,
            CilOperation.BranchIfGreaterThanOrEqualSigned or CilOperation.BranchIfGreaterThanOrEqualUnsigned => (value, bound) => value >= bound,
            CilOperation.BranchIfLessThanSigned or CilOperation.BranchIfLessThanUnsigned => (value, bound) => value < bound,
            CilOperation.BranchIfLessThanOrEqualSigned or CilOperation.BranchIfLessThanOrEqualUnsigned => (value, bound) => value <= bound,
            _ => null,
        };
        switch (test.Operation)
        {
            case CilOperation.Pop when present(at + 1) && instructions[at + 1].Operation == CilOperation.Pop:
                return Outcome.Never;
            case CilOperation.BranchIfEqual:
                return Equal(states, start, test.Operand, taken: true, constant);
            case CilOperation.BranchIfNotEqual:
                return Equal(states, start, test.Operand, taken: false, constant);
        }
        if (ordered is null) return null;
        if ((long)states.Min - start < int.MinValue || (long)states.Max - start > int.MaxValue) return Outcome.Either;
        var unsigned = test.Operation is CilOperation.BranchIfGreaterThanUnsigned or
            CilOperation.BranchIfGreaterThanOrEqualUnsigned or CilOperation.BranchIfLessThanUnsigned or
            CilOperation.BranchIfLessThanOrEqualUnsigned;
        long least, greatest, bound;
        if (unsigned)
        {
            // Unsigned, the shifted states at or above zero (the states from `start` up) order below the negative ones.
            var first = states.FirstAtLeast(start);
            least = unchecked((uint)(first < states.Count ? states[first] - start : states.Min - start));
            greatest = unchecked((uint)(first > 0 ? states[first - 1] - start : states.Max - start));
            bound = unchecked((uint)constant);
        }
        else
        {
            least = states.Min - start;
            greatest = states.Max - start;
            bound = constant;
        }
        var low = ordered(least, bound);
        if (low != ordered(greatest, bound)) return Outcome.Either;
        return low ? Outcome.Always(test.Operand) : Outcome.Never;
    }

    // The decoder's switch chain (`dup; ldc.i4 c; bne.un next; pop; br case` for each case, then `pop`) on a state that
    // can be any of `states`: each state takes the first case it equals, or none. All the same: the chain goes, a branch
    // to that case in its place. Otherwise each case no state takes goes, so nothing kept branches to the resume block of
    // an await whose state the machine no longer has. Cost: a lookup per case.
    private static StateFold FoldChain(ImmutableArray<CilInstruction> instructions, int index, int at, Rewriting rewrite,
        StateSet states, int start)
    {
        bool Present(int position) => position < instructions.Length && !rewrite.Removed[position];
        var links = new List<(int At, int Constant, CilOperand Target)>();
        var position = at;
        while (Present(position + 4) && instructions[position].Operation == CilOperation.Duplicate)
        {
            if (instructions[position + 1] is not { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: var constant } } ||
                instructions[position + 2] is not { Operation: CilOperation.BranchIfNotEqual, Operand: CilOperand.BranchTarget { Offset: var next } } ||
                next != instructions[position + 5 < instructions.Length ? position + 5 : position].Offset ||
                instructions[position + 3].Operation != CilOperation.Pop ||
                instructions[position + 4] is not { Operation: CilOperation.Branch, Operand: var target })
            {
                return StateFold.Unmatched;
            }
            links.Add((position, constant, target));
            position += 5;
        }
        if (links.Count == 0 || !Present(position) || instructions[position].Operation != CilOperation.Pop) return StateFold.Unmatched;
        // A case is taken by the state it names, unless an earlier case names the same one; distinct states take
        // distinct cases, so all take the same one only when there is one state.
        var used = new bool[links.Count];
        var named = new HashSet<int>();
        var taken = 0;
        var last = -1;
        for (var link = 0; link < links.Count; link++)
        {
            if (!named.Add(links[link].Constant) || !states.Contains(unchecked(links[link].Constant + start))) continue;
            used[link] = true;
            taken++;
            last = link;
        }
        if (taken == 0 || (taken == 1 && states.Count == 1))
        {
            Remove(rewrite, index, 1);
            Drop(rewrite, index + 1, position - index);
            if (taken == 1) rewrite.Replaced[index] = [Op(CilOperation.Branch, links[last].Target)];
            return StateFold.Folded;
        }
        for (var link = 0; link < links.Count; link++)
        {
            if (!used[link]) Remove(rewrite, links[link].At, 5);
        }
        return StateFold.Kept;
    }

    // What a test of the state does for every value it can hold: never taken, always taken (to Target), or either.
    private readonly record struct Outcome(bool Taken, bool Mixed, CilOperand? Target)
    {
        public static Outcome Never => new(false, false, null);

        public static Outcome Either => new(false, true, null);

        public static Outcome Always(CilOperand target) => new(true, false, target);
    }

    // The values a read of the state can hold, sorted (for the bounds of an ordered test) and hashed (for an equality).
    internal sealed class StateSet
    {
        private readonly int[] _sorted;
        private readonly HashSet<int> _members;

        internal StateSet(IEnumerable<int> states)
        {
            _members = [.. states];
            if (_members.Count == 0) throw new ArgumentException("a state read holds at least one state", nameof(states));
            _sorted = [.. _members];
            Array.Sort(_sorted);
        }

        internal int Count => _sorted.Length;

        internal int Min => _sorted[0];

        internal int Max => _sorted[^1];

        internal int this[int position] => _sorted[position];

        internal bool Contains(int state) => _members.Contains(state);

        // The position of the first state at least `value`, or Count.
        internal int FirstAtLeast(int value)
        {
            var found = Array.BinarySearch(_sorted, value);
            return found >= 0 ? found : ~found;
        }
    }

    // For each instruction that pops, the instruction that pushed its deepest operand (a field access's object, a call's
    // first argument), or -1 where paths that join pushed it differently or a branch back reaches it with values; null
    // when a backward branch carries a value.
    // One forward pass over a persistent stack of producers: a push links to the slot below, so a branch keeps its
    // stack for its target at no cost, and a join compares two stacks only down to the slot they share.
    internal static int[]? Producers(CilMethodBody body, IReadOnlyDictionary<int, int> depths)
    {
        const int Empty = -1;
        const int Unreached = -2;
        var instructions = body.Instructions;
        var pushedBy = new List<int>();
        var below = new List<int>();
        int Push(int producer, int under)
        {
            pushedBy.Add(producer);
            below.Add(under);
            return pushedBy.Count - 1;
        }
        int Merge(int left, int right)
        {
            if (left == Unreached) return right;
            if (right == Unreached) return left;
            var differing = new List<int>();
            while (left != right && left != Empty && right != Empty)
            {
                differing.Add(pushedBy[left] == pushedBy[right] ? pushedBy[left] : -1);
                left = below[left];
                right = below[right];
            }
            for (var index = differing.Count - 1; index >= 0; index--) left = Push(differing[index], left);
            return left;
        }
        var incoming = new Dictionary<int, int>();
        void Send(int offset, int stack) => incoming[offset] = Merge(incoming.GetValueOrDefault(offset, Unreached), stack);
        foreach (var region in body.ExceptionRegions)
        {
            var exception = region.Kind is CilExceptionRegionKind.Catch or CilExceptionRegionKind.Filter;
            Send(region.HandlerOffset, exception ? Push(-1, Empty) : Empty);
            if (region.FilterOffset is { } filter) Send(filter, Push(-1, Empty));
        }
        var producers = new int[instructions.Length];
        Array.Fill(producers, -1);
        var stack = Empty;
        for (var index = 0; index < instructions.Length; index++)
        {
            var instruction = instructions[index];
            if (incoming.Remove(instruction.Offset, out var joined)) stack = Merge(stack, joined);
            if (!depths.TryGetValue(instruction.Offset, out var depth) ||
                CilStackDepths.Effect(instruction, depth) is not var (pops, pushes))
            {
                stack = Unreached;
                continue;
            }
            // Reached only by a branch back (a loop's body): its slots' producers are unknown here.
            if (stack == Unreached)
            {
                stack = Empty;
                for (var slot = 0; slot < depth; slot++) stack = Push(-1, stack);
            }
            var after = stack;
            for (var pop = 0; pop < pops; pop++)
            {
                if (after == Empty) return null;
                if (pop == pops - 1) producers[index] = pushedBy[after];
                after = below[after];
            }
            for (var push = 0; push < pushes; push++) after = Push(index, after);
            var targets = instruction.Operand switch
            {
                CilOperand.BranchTarget branch => [branch.Offset],
                CilOperand.SwitchTargets table => table.Offsets,
                _ => ImmutableArray<int>.Empty,
            };
            var carried = instruction.Operation == CilOperation.Leave ? Empty : after;
            foreach (var target in targets)
            {
                if (target <= instruction.Offset)
                {
                    if (carried != Empty) return null;
                    continue;
                }
                Send(target, carried);
            }
            stack = instruction.Operation is CilOperation.Branch or CilOperation.Leave or CilOperation.Return or
                CilOperation.Throw or CilOperation.Rethrow or CilOperation.EndFinally or CilOperation.EndFilter
                ? Unreached
                : after;
        }
        return producers;
    }

    // The locals each use of which is a store with its address taken right after, so dropping one such pair loses
    // nothing a later use reads.
    internal static bool[] Temporaries(CilMethodBody body)
    {
        var instructions = body.Instructions;
        var paired = new bool[body.LocalSignatureTypes.Length];
        Array.Fill(paired, true);
        for (var index = 0; index < instructions.Length; index++)
        {
            if (instructions[index] is not
                {
                    Operation: CilOperation.LoadLocal or CilOperation.StoreLocal or CilOperation.LoadLocalAddress,
                    Operand: CilOperand.Index { Value: var local },
                } || (uint)local >= (uint)paired.Length)
            {
                continue;
            }
            var pairs = instructions[index].Operation switch
            {
                CilOperation.StoreLocal => index + 1 < instructions.Length &&
                    instructions[index + 1] is { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var next } } &&
                    next == local,
                CilOperation.LoadLocalAddress => index > 0 &&
                    instructions[index - 1] is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var previous } } &&
                    previous == local,
                _ => false,
            };
            if (!pairs) paired[local] = false;
        }
        return paired;
    }

    // A leave no remaining region encloses is a branch: the catch it left is gone. One sweep, each region's bounds
    // counted at their labels.
    internal static void Unwrap(List<CilBodyElement> elements, ImmutableArray<CilBodyRegion> regions)
    {
        var change = new Dictionary<CilBodyLabel, int>(ReferenceEqualityComparer.Instance);
        void Add(CilBodyLabel label, int delta) => change[label] = change.GetValueOrDefault(label) + delta;
        foreach (var region in regions)
        {
            Add(region.TryStart, 1);
            Add(region.TryEnd, -1);
            Add(region.FilterStart ?? region.HandlerStart, 1);
            Add(region.HandlerEnd, -1);
        }
        var depth = 0;
        for (var index = 0; index < elements.Count; index++)
        {
            if (elements[index] is CilBodyElement.Mark mark)
                depth += change.GetValueOrDefault(mark.Label);
            else if (depth == 0 && elements[index] is CilBodyElement.Branch { Operation: CilOperation.Leave } leave)
                elements[index] = leave with { Operation = CilOperation.Branch };
        }
    }

    // The instructions control reaches from the start or a handler: the others go (the resume points' code that the
    // dispatch alone reached, and code after a folded branch).
    internal static IEnumerable<CilBodyElement> Reachable(List<CilBodyElement> elements, ImmutableArray<CilBodyRegion> regions)
    {
        var at = new Dictionary<CilBodyLabel, int>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < elements.Count; index++)
        {
            if (elements[index] is CilBodyElement.Mark mark) at[mark.Label] = index;
        }
        var reached = new bool[elements.Count];
        var pending = new Stack<int>();
        pending.Push(0);
        foreach (var region in regions)
        {
            pending.Push(at[region.HandlerStart]);
            if (region.FilterStart is { } filter) pending.Push(at[filter]);
        }
        while (pending.TryPop(out var index))
        {
            if (index >= elements.Count || reached[index]) continue;
            reached[index] = true;
            switch (elements[index])
            {
                case CilBodyElement.Branch branch:
                    pending.Push(at[branch.Target]);
                    if (branch.Operation is CilOperation.Branch or CilOperation.Leave) continue;
                    break;
                case CilBodyElement.Switch table:
                    foreach (var target in table.Targets) pending.Push(at[target]);
                    break;
                case CilBodyElement.Instruction
                {
                    Operation: CilOperation.Return or CilOperation.Throw or CilOperation.Rethrow or CilOperation.EndFinally or
                    CilOperation.EndFilter,
                }:
                    continue;
            }
            pending.Push(index + 1);
        }
        return elements.Where((element, index) => element is CilBodyElement.Mark || reached[index]);
    }

    // A replacement written against MoveNext's numbering, in the draft's terms: its locals and labels.
    internal static CilBodyElement Labeled(CilBodyElement element, ImmutableArray<CilBodyLocal> locals,
        ImmutableDictionary<int, CilBodyLabel> labels) => element switch
        {
            CilBodyElement.Instruction
            {
                Operation: CilOperation.LoadLocal or CilOperation.StoreLocal or CilOperation.LoadLocalAddress,
                Operand: CilOperand.Index { Value: var local },
            } instruction => new CilBodyElement.LocalInstruction(instruction.Operation, locals[local]),
            CilBodyElement.Instruction { Operand: CilOperand.BranchTarget branch } instruction =>
                new CilBodyElement.Branch(instruction.Operation, labels[branch.Offset]),
            _ => element,
        };

    internal static bool IsThis(CilInstruction instruction) =>
        instruction is { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: 0 } };

    internal static bool IsBuilderCall(CilInstruction instruction, string name) =>
        instruction is { Operation: CilOperation.Call, Operand: CilOperand.MethodInstance { Value: var method } } &&
        method.Definition.Name == name &&
        (AsyncStateMachineKickoffs.TakesBuilder(method.DeclaringType) || AsyncStateMachineKickoffs.TakesIteratorBuilder(method.DeclaringType));

    internal static string? DefinitionName(CliTypeIdentity type) =>
        type.Shape == CliTypeShape.GenericInstantiation ? type.ElementType?.FullName : type.FullName;

    // Instructions whose effect, if any, the rewrite takes over elsewhere.
    internal static void Remove(Rewriting rewrite, int start, int count) => Array.Fill(rewrite.Removed, true, start, count);

    // Instructions that go with their block, so nothing kept may branch to them.
    internal static void Drop(Rewriting rewrite, int start, int count)
    {
        Array.Fill(rewrite.Removed, true, start, count);
        Array.Fill(rewrite.Blocked, true, start, count);
    }

    // What the rewrite does to each of MoveNext's instructions: removed, blocked as well, or replaced.
    internal sealed class Rewriting(int length)
    {
        public bool[] Removed { get; } = new bool[length];

        public bool[] Blocked { get; } = new bool[length];

        public Dictionary<int, List<CilBodyElement>> Replaced { get; } = [];
    }

    // What a fold of a state read did: folded the test away, kept it (it tells the states apart), or did not know it.
    internal enum StateFold
    {
        Folded,
        Kept,
        Unmatched,
    }
}
