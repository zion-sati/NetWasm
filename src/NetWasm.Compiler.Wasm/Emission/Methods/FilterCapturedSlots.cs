// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// The locals and arguments a method's exception filters capture: each one a filter body names, and each root a
// safepoint inside a filter body reads. The filter funclet reads them from the filter environment, which the method
// fills from their frame slots, so they need frame storage. Null when the method has no filter.
internal static class FilterCapturedSlots
{
    public static ImmutableHashSet<CapturedSlot>? Find(StructuredMethod method, IExceptionGroupEnumerator exceptionGroups,
        MethodRootMap rootMap)
    {
        var slots = ImmutableHashSet.CreateBuilder<CapturedSlot>();
        var hasFilters = false;
        foreach (var clause in exceptionGroups.Enumerate(method)
                     .Select(group => method.ExceptionGroups[group])
                     .SelectMany(group => group.Clauses))
        {
            if (clause.Kind != CilExceptionRegionKind.Filter ||
                clause.FilterBlock is not { })
            {
                continue;
            }
            hasFilters = true;
            foreach (var instruction in EnumerateInstructions(method, clause.FilterBody))
            {
                bool? isArgument = instruction.Operation switch
                {
                    CilOperation.LoadArgument or CilOperation.StoreArgument or
                        CilOperation.LoadArgumentAddress => true,
                    CilOperation.LoadLocal or CilOperation.StoreLocal or
                        CilOperation.LoadLocalAddress => false,
                    _ => null,
                };
                if (isArgument is bool argument)
                {
                    slots.Add(new CapturedSlot(
                        argument,
                        CilOperandReader.GetIndex(instruction)));
                }
                if (rootMap.Safepoints.TryGetValue(instruction.Offset, out var safepoint))
                {
                    foreach (var source in safepoint.Roots.Concat(safepoint.ConstructorCallRoots))
                    {
                        CapturedSlot? root = source.Kind switch
                        {
                            RootSourceKind.Argument or RootSourceKind.ManagedAddressArgument => new(true, source.Index),
                            RootSourceKind.Local or RootSourceKind.ManagedAddressLocal => new(false, source.Index),
                            _ => null,
                        };
                        if (root is { } captured) slots.Add(captured);
                    }
                }
            }
        }
        return hasFilters ? slots.ToImmutable() : null;
    }

    // The instructions of a filter body's blocks, each block once.
    public static IEnumerable<CilInstruction> EnumerateInstructions(
        StructuredMethod method,
        StructuredSequence? sequence)
    {
        if (sequence is null)
        {
            yield break;
        }

        var seen = new HashSet<StructuredBlockId>();
        foreach (var block in EnumerateBlocks(sequence))
        {
            if (!seen.Add(block))
            {
                continue;
            }
            foreach (var instruction in method.Blocks[block].Instructions)
            {
                yield return instruction;
            }
        }
    }

    private static IEnumerable<StructuredBlockId> EnumerateBlocks(
        StructuredSequence sequence)
    {
        foreach (var region in sequence.Regions)
        {
            switch (region)
            {
                case StructuredCode code:
                    yield return code.Occurrence.Block;
                    break;
                case StructuredSwitch table:
                    yield return table.Head.Block;
                    foreach (var block in table.Chain)
                    {
                        yield return block.Block;
                    }
                    break;
                case StructuredIf conditional:
                    yield return conditional.Condition.Block;
                    foreach (var block in EnumerateBlocks(conditional.WhenTrue))
                    {
                        yield return block;
                    }
                    foreach (var block in EnumerateBlocks(conditional.WhenFalse))
                    {
                        yield return block;
                    }
                    break;
                case StructuredLoop loop:
                    if (loop.Condition is { } condition)
                    {
                        yield return condition.Block;
                    }
                    foreach (var block in EnumerateBlocks(loop.Body))
                    {
                        yield return block;
                    }
                    foreach (var block in EnumerateBlocks(loop.ContinueBody))
                    {
                        yield return block;
                    }
                    foreach (var block in EnumerateBlocks(loop.ExitBody))
                    {
                        yield return block;
                    }
                    break;
                case StructuredPostTestLoop loop:
                    foreach (var block in EnumerateBlocks(loop.Body))
                    {
                        yield return block;
                    }
                    yield return loop.Condition.Block;
                    foreach (var block in EnumerateBlocks(loop.ContinueBody))
                    {
                        yield return block;
                    }
                    foreach (var block in EnumerateBlocks(loop.ExitBody))
                    {
                        yield return block;
                    }
                    break;
                case StructuredDispatcher dispatcher:
                    foreach (var block in dispatcher.Blocks)
                    {
                        yield return block.Occurrence.Block;
                    }
                    foreach (var exit in dispatcher.Exits)
                    {
                        foreach (var block in EnumerateBlocks(exit.Body))
                        {
                            yield return block;
                        }
                    }
                    break;
                case StructuredLabeledBlock labeled:
                    foreach (var block in EnumerateBlocks(labeled.Body))
                    {
                        yield return block;
                    }
                    break;
                case StructuredLabeledLoop labeled:
                    foreach (var block in EnumerateBlocks(labeled.Body))
                    {
                        yield return block;
                    }
                    break;
                case StructuredExceptionRegion:
                case StructuredLoopBreak:
                case StructuredLoopContinue:
                case StructuredDispatcherContinue:
                case StructuredLabelBranch:
                    break;
            }
        }
    }
}
