using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

// Strategy: splice already planned query fragments into a method, retaining its
// control-flow, exception boundaries, local signatures and source locations.
internal sealed class AttributeQueryBodyRewriter : IAttributeQueryBodyRewriter
{
    public CilMethodBody Rewrite(
        CilMethodBody body,
        ImmutableDictionary<int, AttributeCilFragment> replacements)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(replacements);
        if (replacements.IsEmpty)
        {
            return body;
        }

        var starts = new Dictionary<int, int>();
        var length = 0;
        var found = 0;
        var additionalStack = 0;
        foreach (var instruction in body.Instructions)
        {
            starts.Add(instruction.Offset, length);
            if (replacements.TryGetValue(instruction.Offset, out var fragment))
            {
                if (fragment.Instructions.IsDefaultOrEmpty)
                {
                    throw new ArgumentException("A query replacement must contain instructions.", nameof(replacements));
                }
                found++;
                length = checked(length + fragment.Instructions.Length);
                additionalStack = Math.Max(additionalStack, fragment.MaxStack);
            }
            else
            {
                length = checked(length + 1);
            }
        }
        if (found != replacements.Count)
        {
            throw new ArgumentException("A query replacement does not identify an instruction.", nameof(replacements));
        }
        starts.Add(body.Instructions[^1].NextOffset, length);

        var instructions = ImmutableArray.CreateBuilder<CilInstruction>(length);
        var localTypes = body.LocalSignatureTypes.ToBuilder();
        foreach (var instruction in body.Instructions)
        {
            if (replacements.TryGetValue(instruction.Offset, out var fragment))
            {
                var start = instructions.Count;
                foreach (var inserted in fragment.Instructions)
                {
                    instructions.Add(inserted with
                    {
                        Offset = instructions.Count,
                        NextOffset = instructions.Count + 1,
                        Operand = inserted.Operand switch
                        {
                            CilOperand.BranchTarget target => new CilOperand.BranchTarget(start + target.Offset),
                            CilOperand.SwitchTargets targets => new CilOperand.SwitchTargets(
                                [.. targets.Offsets.Select(offset => start + offset)]),
                            _ => inserted.Operand,
                        },
                        OriginalOffset = instruction.SourceOffset,
                    });
                }
                localTypes.AddRange(fragment.LocalTypes);
            }
            else
            {
                instructions.Add(instruction with
                {
                    Offset = instructions.Count,
                    NextOffset = instructions.Count + 1,
                    Operand = instruction.Operand switch
                    {
                        CilOperand.BranchTarget target => new CilOperand.BranchTarget(starts[target.Offset]),
                        CilOperand.SwitchTargets targets => new CilOperand.SwitchTargets(
                            [.. targets.Offsets.Select(offset => starts[offset])]),
                        _ => instruction.Operand,
                    },
                    OriginalOffset = instruction.SourceOffset,
                });
            }
        }

        return body with
        {
            Instructions = instructions.MoveToImmutable(),
            LocalSignatureTypes = localTypes.ToImmutable(),
            Locals = [.. localTypes.Select(type => type.StackKind)],
            // Existing stack values can survive beneath the evaluated query
            // arguments. Reserve room for those as well as fragment temporaries.
            MaxStack = checked(body.MaxStack + additionalStack),
            ExceptionRegions = [.. body.ExceptionRegions.Select(region => region with
            {
                TryOffset = starts[region.TryOffset],
                TryLength = starts[region.TryOffset + region.TryLength] - starts[region.TryOffset],
                HandlerOffset = starts[region.HandlerOffset],
                HandlerLength = starts[region.HandlerOffset + region.HandlerLength] - starts[region.HandlerOffset],
                FilterOffset = region.FilterOffset is { } filter ? starts[filter] : null,
            })],
        };
    }
}
