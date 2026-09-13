using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class FilterEnvironmentLayoutPlanner(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IArgumentSignatureTypeResolver types,
    IExceptionGroupEnumerator exceptionGroups) : IFilterEnvironmentLayoutPlanner
{
    public FilterEnvironmentLayout Create(
        StructuredMethod method,
        ValueFrameLayout valueLayout)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(valueLayout);

        var header = method.Header;
        var slots = new HashSet<CapturedSlot>();
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
            }
        }
        if (!hasFilters)
        {
            return FilterEnvironmentLayout.Empty;
        }

        var size = valueLayout.Size;
        var rootFrameOffset = size;
        size = checked(size + layouts.Target.AddressSize);
        var rootSlot = 0;
        var captures = ImmutableDictionary.CreateBuilder<CapturedSlot, FilterCapture>();
        foreach (var slot in slots
                     .OrderBy(slot => slot.IsArgument ? 0 : 1)
                     .ThenBy(slot => slot.Index))
        {
            var type = slot.IsArgument
                ? types.Resolve(header, slot.Index)
                : header.LocalSignatureTypes[slot.Index];
            int offset;
            if (!slot.IsArgument && type.StackKind == CliValueKind.ValueType)
            {
                offset = valueLayout.LocalOffsets[slot.Index];
            }
            else
            {
                var alignment = type.StackKind == CliValueKind.ValueType
                    ? values.GetValueLayout(type).Alignment
                    : layouts.Target.GetStorageAlignment(type);
                var byteSize = type.StackKind == CliValueKind.ValueType
                    ? values.GetValueLayout(type).Size
                    : layouts.Target.GetStorageSize(type);
                size = Align(size, alignment);
                offset = size;
                size = checked(size + byteSize);
            }

            var referenceOffsets = type.StackKind switch
            {
                CliValueKind.ManagedReference => [0],
                CliValueKind.ValueType => values.GetValueLayout(type).ReferenceOffsets,
                _ => [],
            };
            var roots = referenceOffsets
                .Select(referenceOffset => (referenceOffset, rootSlot++))
                .ToImmutableArray();
            captures.Add(slot, new FilterCapture(slot, type, offset, roots));
        }
        return new FilterEnvironmentLayout(
            Align(size, layouts.Target.ObjectReferenceAlignment),
            rootSlot,
            rootFrameOffset,
            captures.ToImmutable());
    }

    private static int Align(int value, int alignment) =>
        checked((value + alignment - 1) & -alignment);

    private static IEnumerable<CilInstruction> EnumerateInstructions(
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
                    yield return loop.Condition.Block;
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
                case StructuredExceptionRegion:
                case StructuredLoopBreak:
                case StructuredLoopContinue:
                case StructuredDispatcherContinue:
                    break;
            }
        }
    }
}
