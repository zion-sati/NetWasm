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
// A method whose body is a leading if, and whose frames only one side of it needs, enters them
// on that side. Either both arms flow into a straight-line tail that ends the method, which each
// arm then carries, or one arm always returns or throws, so what follows the if belongs to the
// other arm alone. The root frame moves with the value frame when no safepoint outside the framed
// side holds a root. An argument the frame holds is copied in where the framed side begins, so
// only that side may take its address; the other side reads the parameter.
internal sealed class TerminalBranchValueFramePlanner(IArgumentSignatureTypeResolver argumentTypes)
    : ITerminalBranchValueFramePlanner
{
    public TerminalBranchValueFramePlan? Plan(StructuredMethod method, ValueFrameLayout layout, MethodRootMap roots)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(roots);

        if ((layout.Size == 0 && roots.SlotCount == 0) || !method.ExceptionGroups.IsEmpty ||
            // A value-type argument's parameter becomes its frame copy's address at frame entry.
            layout.ArgumentOffsets.Keys.Any(index =>
                argumentTypes.Resolve(method.Header, index).StackKind == CliValueKind.ValueType) ||
            !layout.NativeArgumentOffsets.IsEmpty ||
            !layout.MemberResultOffsets.IsEmpty || !layout.CallReceiverOffsets.IsEmpty ||
            method.Body.Regions.Length < 2 || method.Body.Regions[0] is not StructuredIf branch)
        {
            return null;
        }

        var first = new HashSet<StructuredBlockId>();
        var second = new HashSet<StructuredBlockId>();
        if (!Collect(branch.WhenTrue, first) || !Collect(branch.WhenFalse, second))
        {
            return null;
        }
        var tail = method.Body.Regions.Skip(1).ToImmutableArray();
        var tailBlocks = new HashSet<StructuredBlockId>();
        if (!tail.All(region => Collect(region, tailBlocks)))
        {
            return null;
        }

        // An arm that always leaves the method never reaches the tail, so the tail belongs to the
        // other arm and is not copied; otherwise the tail must end the method in straight-line code.
        var firstLeaves = Leaves(method, branch.WhenTrue);
        var secondLeaves = Leaves(method, branch.WhenFalse);
        var early = firstLeaves != secondLeaves;
        if (!early && (firstLeaves || !IsTerminalStraightLine(method, tail)))
        {
            return null;
        }
        if (early)
        {
            (secondLeaves ? first : second).UnionWith(tailBlocks);
        }
        var common = early ? [branch.Condition.Block] : tailBlocks.Append(branch.Condition.Block).ToHashSet();

        var scalars = layout.SpilledScalarLocals;
        if (scalars.Any(index => !layout.LocalOffsets.ContainsKey(index) ||
                method.Header.LocalSignatureTypes[index].StackKind is not
                    (CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8 or
                        CliValueKind.NativeInt) ||
                IsNarrowStorage(method.Header.LocalSignatureTypes[index])))
        {
            return null;
        }

        var required = new HashSet<StructuredBlockId>();
        var temporaryOffsets = layout.TemporaryOffsets.Keys.ToHashSet();
        var observedTemporaries = new HashSet<int>();
        foreach (var block in first.Concat(second).Concat(common).Distinct())
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

        // The safepoints that hold roots, by the blocks they are in.
        var blockOf = new Dictionary<int, StructuredBlockId>();
        foreach (var id in first.Concat(second).Concat(common).Distinct())
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
        // The root frame moves only where every root-holding safepoint is on the framed side.
        var deferRoots = roots.SlotCount != 0 && !rootedBlocks.Overlaps(common) &&
            ((rootedBlocks.IsSubsetOf(first) && !rootedBlocks.Overlaps(second) && !required.Overlaps(second)) ||
             (rootedBlocks.IsSubsetOf(second) && !rootedBlocks.Overlaps(first) && !required.Overlaps(first)));
        if (deferRoots)
        {
            required.UnionWith(rootedBlocks);
        }
        if (required.Count == 0 || required.Overlaps(common) || (layout.Size == 0 && !deferRoots))
        {
            return null;
        }
        var frameFirst = required.IsSubsetOf(first) && !required.Overlaps(second);
        var frameSecond = required.IsSubsetOf(second) && !required.Overlaps(first);
        if (!frameFirst && !frameSecond)
        {
            return null;
        }

        var unframed = (frameFirst ? second : first).Concat(common).ToHashSet();
        if (!deferRoots && rooted.Where(point => unframed.Contains(blockOf[point.IlOffset]))
            .SelectMany(point => point.Roots.Concat(point.ConstructorCallRoots))
            .Any(root => root.Kind == RootSourceKind.StackStorageAnchor ||
                root.Kind is RootSourceKind.Local or RootSourceKind.ManagedAddressLocal &&
                layout.LocalOffsets.ContainsKey(root.Index)))
        {
            return null;
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
        };
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

    private static bool Collect(StructuredRegion region, HashSet<StructuredBlockId> blocks)
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
            case StructuredLoopBreak or StructuredLoopContinue or StructuredDispatcherContinue:
                return true;
            default:
                return false;
        }
        return children.All(child => Collect(child, blocks));
    }
}
