// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// Plans storage placement only. Root publication and exception ownership remain
// separate emission contracts; a plan does not authorize changing either one.
//
// A method whose body is an if, after regions that need no frame, and whose frames only one side of
// it needs, enters them on that side. Either one arm always returns or throws, so what follows the if belongs to the
// other arm alone; or both arms flow into a straight-line tail that ends the method, which each
// arm then carries where tails may be copied; or, when the method needs only a root frame and no
// safepoint after the if holds a root, the framed arm leaves the frame where it ends and the code
// after the if runs without one, which copies nothing (`if (full) Grow(); buffer[n++] = b;`). The
// root frame moves with the value frame when no safepoint outside the framed side holds a root. An
// argument the frame holds is copied in where the framed side begins, so only that side may take
// its address; the other side reads the parameter.
internal sealed class TerminalBranchValueFramePlanner(IArgumentSignatureTypeResolver argumentTypes)
    : ITerminalBranchValueFramePlanner
{
    public TerminalBranchValueFramePlan? Plan(StructuredMethod method, ValueFrameLayout layout, MethodRootMap roots,
        bool copyTails)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(roots);

        // Without tail copies (Os, Oz) only a root frame moves: a value frame placed in an arm leaves the
        // roots it holds to the root frame, whose publication at every safepoint costs more code than
        // the move saves.
        if ((layout.Size == 0 && roots.SlotCount == 0) || (!copyTails && layout.Size != 0) ||
            !method.ExceptionGroups.IsEmpty ||
            // A value-type argument's parameter becomes its frame copy's address at frame entry.
            layout.ArgumentOffsets.Keys.Any(index =>
                argumentTypes.Resolve(method.Header, index).StackKind == CliValueKind.ValueType) ||
            !layout.NativeArgumentOffsets.IsEmpty ||
            !layout.MemberResultOffsets.IsEmpty || !layout.CallReceiverOffsets.IsEmpty ||
            method.Body.Regions.Length < 2)
        {
            return null;
        }

        var scalars = layout.SpilledScalarLocals;
        if (scalars.Any(index => !layout.LocalOffsets.ContainsKey(index) ||
                method.Header.LocalSignatureTypes[index].StackKind is not
                    (CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8 or
                        CliValueKind.NativeInt) ||
                IsNarrowStorage(method.Header.LocalSignatureTypes[index])))
        {
            return null;
        }

        // The blocks of each top-level region, those that need the value frame, and the safepoints that
        // hold roots by their blocks.
        var regions = method.Body.Regions;
        var regionBlocks = new HashSet<StructuredBlockId>[regions.Length];
        for (var index = 0; index < regions.Length; index++)
        {
            regionBlocks[index] = [];
            if (!Collect(regions[index], regionBlocks[index]))
            {
                return null;
            }
        }
        var all = regionBlocks.SelectMany(blocks => blocks).ToHashSet();
        var required = new HashSet<StructuredBlockId>();
        var temporaryOffsets = layout.TemporaryOffsets.Keys.ToHashSet();
        var observedTemporaries = new HashSet<int>();
        foreach (var block in all)
        {
            foreach (var instruction in method.Blocks[block].Instructions)
            {
                if (instruction.Operation == CilOperation.LocalAllocate)
                {
                    return null;
                }
                if (temporaryOffsets.Contains(instruction.Offset))
                {
                    observedTemporaries.Add(instruction.Offset);
                    required.Add(block);
                }
                if (instruction.Operation == CilOperation.LoadArgumentAddress &&
                    layout.ArgumentOffsets.ContainsKey(CilOperandReader.GetIndex(instruction)))
                {
                    required.Add(block);
                    continue;
                }
                if (instruction.Operation is not (CilOperation.LoadLocal or
                    CilOperation.StoreLocal or CilOperation.LoadLocalAddress))
                {
                    continue;
                }
                var local = CilOperandReader.GetIndex(instruction);
                if (layout.LocalOffsets.ContainsKey(local) &&
                    (!scalars.Contains(local) || instruction.Operation == CilOperation.LoadLocalAddress))
                {
                    required.Add(block);
                }
            }
        }
        if (!temporaryOffsets.SetEquals(observedTemporaries))
        {
            return null;
        }
        var blockOf = new Dictionary<int, StructuredBlockId>();
        foreach (var id in all)
        {
            foreach (var offset in Offsets(method.Blocks[id]))
            {
                blockOf[offset] = id;
            }
        }
        var rooted = roots.Safepoints.Values
            .Where(point => !point.Roots.IsEmpty || !point.ConstructorCallRoots.IsEmpty)
            .ToArray();
        var rootedBlocks = new HashSet<StructuredBlockId>();
        foreach (var point in rooted)
        {
            if (!blockOf.TryGetValue(point.IlOffset, out var id))
            {
                return null;
            }
            rootedBlocks.Add(id);
        }

        // The if goes as deep into the body as it can: the regions before the first that needs a frame run
        // without one (the checks a method makes of its arguments before it does anything, as
        // `if (buffer == null) throw ...; if (n <= 0) return 0;` before `if (n <= 8) copy bytes; else
        // Buffer.BlockCopy(...)`), and that region, an if, places the frames in its arms. Failing that, the
        // body's leading if does.
        var first = Array.FindIndex(regionBlocks, blocks => blocks.Overlaps(required) || blocks.Overlaps(rootedBlocks));
        return first > 0 && first < regions.Length - 1 ? PlanAt(first) ?? PlanAt(0) : PlanAt(0);

        TerminalBranchValueFramePlan? PlanAt(int at)
        {
            if (regions[at] is not StructuredIf branch)
            {
                return null;
            }
            var firstArm = new HashSet<StructuredBlockId>();
            var secondArm = new HashSet<StructuredBlockId>();
            Collect(branch.WhenTrue, firstArm);
            Collect(branch.WhenFalse, secondArm);
            var tail = regions.Skip(at + 1).ToImmutableArray();
            var tailBlocks = regionBlocks.Skip(at + 1).SelectMany(blocks => blocks).ToHashSet();
            var prefix = regions.Take(at).ToImmutableArray();
            var prefixBlocks = regionBlocks.Take(at).SelectMany(blocks => blocks).ToHashSet();

            // An arm that always leaves the method never reaches the tail, so the tail belongs to the
            // other arm and is not copied; otherwise each arm carries a copy of a tail that ends the
            // method in straight-line code, or the framed arm leaves its frame before the tail.
            var firstLeaves = Leaves(method, branch.WhenTrue);
            var secondLeaves = Leaves(method, branch.WhenFalse);
            var early = firstLeaves != secondLeaves;
            if (!early && firstLeaves)
            {
                return null;
            }
            var leavesBeforeTail = !early && !(copyTails && IsTerminalStraightLine(method, tail));
            if (early)
            {
                (secondLeaves ? firstArm : secondArm).UnionWith(tailBlocks);
            }
            // What runs on both paths: the regions before the if, its condition and, unless an arm leaves, the
            // tail after it.
            var common = (early ? [branch.Condition.Block] : tailBlocks.Append(branch.Condition.Block))
                .Concat(prefixBlocks).ToHashSet();

            // The root frame moves only where every root-holding safepoint is on the framed side.
            var deferRoots = roots.SlotCount != 0 && !rootedBlocks.Overlaps(common) &&
                ((rootedBlocks.IsSubsetOf(firstArm) && !rootedBlocks.Overlaps(secondArm) && !required.Overlaps(secondArm)) ||
                 (rootedBlocks.IsSubsetOf(secondArm) && !rootedBlocks.Overlaps(firstArm) && !required.Overlaps(firstArm)));
            // A frame left before the tail can hold only roots: nothing copies a value frame's
            // contents back into the locals the tail would read.
            if (leavesBeforeTail && (!deferRoots || layout.Size != 0))
            {
                return null;
            }
            var framed = new HashSet<StructuredBlockId>(required);
            if (deferRoots)
            {
                framed.UnionWith(rootedBlocks);
            }
            if (framed.Count == 0 || framed.Overlaps(common) || (layout.Size == 0 && !deferRoots))
            {
                return null;
            }
            var frameFirst = framed.IsSubsetOf(firstArm) && !framed.Overlaps(secondArm);
            var frameSecond = framed.IsSubsetOf(secondArm) && !framed.Overlaps(firstArm);
            if (!frameFirst && !frameSecond)
            {
                return null;
            }

            var unframed = (frameFirst ? secondArm : firstArm).Concat(common).ToHashSet();
            if (!deferRoots && rooted.Where(point => unframed.Contains(blockOf[point.IlOffset]))
                .SelectMany(point => point.Roots.Concat(point.ConstructorCallRoots))
                .Any(root => root.Kind == RootSourceKind.StackStorageAnchor ||
                    root.Kind is RootSourceKind.Local or RootSourceKind.ManagedAddressLocal &&
                    layout.LocalOffsets.ContainsKey(root.Index)))
            {
                return null;
            }

            var unframedPrefix = prefix.IsEmpty ? null : new StructuredSequence(prefix);
            if (leavesBeforeTail)
            {
                return new(branch.Condition, branch.WhenTrue, branch.WhenFalse, frameFirst, scalars)
                {
                    DefersRootFrame = true,
                    UnframedPrefix = unframedPrefix,
                    UnframedTail = new(tail),
                };
            }
            if (early)
            {
                return new(branch.Condition,
                    secondLeaves ? new([.. branch.WhenTrue.Regions, .. tail]) : branch.WhenTrue,
                    firstLeaves ? new([.. branch.WhenFalse.Regions, .. tail]) : branch.WhenFalse,
                    frameFirst,
                    scalars)
                {
                    DefersRootFrame = deferRoots,
                    UnframedPrefix = unframedPrefix,
                };
            }
            // One copy retains ownership; the other is explicitly an executing
            // replica, preserving the original block-accounting contract.
            var replicaTail = tail.Cast<StructuredCode>().Select(code => (StructuredRegion)(code with
            {
                Occurrence = code.Occurrence with { Role = StructuredBlockRole.ExecutingReplica },
            }));
            return new(branch.Condition,
                new([.. branch.WhenTrue.Regions, .. tail]),
                new([.. branch.WhenFalse.Regions, .. replicaTail]),
                frameFirst,
                scalars)
            {
                DefersRootFrame = deferRoots,
                UnframedPrefix = unframedPrefix,
            };
        }
    }

    // A tail the arms can each carry: straight-line blocks in order, each entered with an empty
    // stack and flowing or jumping into the next, the last returning or throwing. The tail holds
    // at least the region after the leading if.
    private static bool IsTerminalStraightLine(StructuredMethod method, ImmutableArray<StructuredRegion> tail)
    {
        if (!tail.All(region => region is StructuredCode code && method.Blocks[code.Occurrence.Block].EntryStack.IsEmpty))
        {
            return false;
        }
        var blocks = tail.Select(static region => ((StructuredCode)region).Occurrence.Block).ToArray();
        for (var index = 0; index + 1 < blocks.Length; index++)
        {
            var target = method.Blocks[blocks[index]].Exit switch
            {
                StructuredFallthroughExit flow => flow.Target,
                StructuredBranchExit jump => jump.Target,
                _ => (StructuredBlockId?)null,
            };
            if (target != blocks[index + 1]) return false;
        }
        return method.Blocks[blocks[^1]].Exit is StructuredTerminalExit
        { Instruction.Operation: CilOperation.Return or CilOperation.Throw };
    }

    // Whether every path through the arm returns or throws: its last region does.
    private static bool Leaves(StructuredMethod method, StructuredRegion region) => region switch
    {
        StructuredCode code => method.Blocks[code.Occurrence.Block].Exit is StructuredTerminalExit
        { Instruction.Operation: CilOperation.Return or CilOperation.Throw },
        StructuredSequence sequence => sequence.Regions.Length != 0 && Leaves(method, sequence.Regions[^1]),
        StructuredIf branch => Leaves(method, branch.WhenTrue) && Leaves(method, branch.WhenFalse),
        _ => false,
    };

    // The IL offsets a block's instructions and exit carry, which safepoints are keyed by.
    private static IEnumerable<int> Offsets(StructuredBlockDefinition block)
    {
        foreach (var instruction in block.Instructions)
        {
            yield return instruction.Offset;
        }
        switch (block.Exit)
        {
            case StructuredTerminalExit terminal:
                yield return terminal.Instruction.Offset;
                break;
            case StructuredConditionalExit conditional:
                yield return conditional.Condition.InstructionOffset;
                break;
        }
    }

    private static bool IsNarrowStorage(CliTypeIdentity type) =>
        // Match the narrow sign/zero-extending loads used by ManagedMemoryEmitter.
        // Wasm stack-slot size alone does not encode those storage semantics.
        (type.StackStorageType ?? type).CanonicalName is
            "primitive:i1" or "primitive:u1" or "primitive:i2" or "primitive:u2" or
            "primitive:bool" or "primitive:char";

    // The blocks an arm or the tail runs, or false for a region the plan does not take. A branch to a label goes
    // only to one the same arm opened (a flattened else-if chain's, or a switch table's): a framed arm releases its
    // frame where it ends, and a branch to a label outside it would leave without doing so.
    private static bool Collect(StructuredRegion region, HashSet<StructuredBlockId> blocks) =>
        Collect(region, blocks, []);

    private static bool Collect(StructuredRegion region, HashSet<StructuredBlockId> blocks,
        HashSet<StructuredBlockId> labels)
    {
        IEnumerable<StructuredRegion> children;
        switch (region)
        {
            case StructuredCode code:
                blocks.Add(code.Occurrence.Block);
                return true;
            case StructuredSequence sequence:
                children = sequence.Regions;
                break;
            case StructuredIf branch:
                blocks.Add(branch.Condition.Block);
                children = [branch.WhenTrue, branch.WhenFalse];
                break;
            case StructuredLoop loop:
                if (loop.Condition is { } condition)
                {
                    blocks.Add(condition.Block);
                }
                children = [loop.Body, loop.ContinueBody, loop.ExitBody];
                break;
            case StructuredPostTestLoop loop:
                blocks.Add(loop.Condition.Block);
                children = [loop.Body, loop.ContinueBody, loop.ExitBody];
                break;
            case StructuredDispatcher dispatcher:
                blocks.UnionWith(dispatcher.Blocks.Select(block => block.Occurrence.Block));
                children = dispatcher.Exits.Select(exit => exit.Body);
                break;
            case StructuredLabeledBlock labeled:
                labels.Add(labeled.Next);
                children = [labeled.Body];
                break;
            case StructuredLabeledLoop loop:
                labels.Add(loop.Header);
                children = [loop.Body];
                break;
            case StructuredLabelBranch branch:
                return labels.Contains(branch.Target);
            case StructuredSwitch table:
                blocks.Add(table.Head.Block);
                blocks.UnionWith(table.Chain.Select(block => block.Block));
                return table.Cases.All(labels.Contains) && labels.Contains(table.Default);
            case StructuredLoopBreak or StructuredLoopContinue or StructuredDispatcherContinue:
                return true;
            default:
                return false;
        }
        return children.All(child => Collect(child, blocks, labels));
    }
}
