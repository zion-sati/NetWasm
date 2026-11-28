// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

// A decoded body read back as a draft, and the label of each of its instruction offsets (and of its end), which a
// rewrite targets or inserts code before.
internal sealed record CilBodyDraftReading(CilBodyDraft Draft, ImmutableDictionary<int, CilBodyLabel> Labels);

// Reads a decoded body back into a draft, so a rewrite can insert code and add locals without renumbering offsets itself:
// each instruction keeps its source offset, branch and switch targets and region bounds become labels, and the declared
// locals keep their order, types and pinning. Laying the draft out again gives the same body at consecutive offsets.
internal static class CilBodyDrafts
{
    public static CilBodyDraftReading Read(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var end = body.Instructions.IsEmpty ? 0 : body.Instructions[^1].NextOffset;
        var labels = body.Instructions.Select(instruction => instruction.Offset).Append(end).Distinct()
            .ToImmutableDictionary(offset => offset, _ => new CilBodyLabel());
        var locals = body.LocalSignatureTypes
            .Select((type, index) => new CilBodyLocal(type, body.PinnedLocals.Contains(index)))
            .ToImmutableArray();
        var elements = ImmutableArray.CreateBuilder<CilBodyElement>(body.Instructions.Length * 2 + 1);
        foreach (var instruction in body.Instructions)
        {
            elements.Add(new CilBodyElement.Mark(labels[instruction.Offset]));
            CilBodyElement element = (instruction.Operation, instruction.Operand) switch
            {
                (_, CilOperand.BranchTarget target) => new CilBodyElement.Branch(instruction.Operation, labels[target.Offset]),
                (_, CilOperand.SwitchTargets targets) =>
                    new CilBodyElement.Switch([.. targets.Offsets.Select(offset => labels[offset])]),
                (CilOperation.LoadLocal or CilOperation.StoreLocal or CilOperation.LoadLocalAddress,
                    CilOperand.Index index) => new CilBodyElement.LocalInstruction(instruction.Operation, locals[index.Value]),
                _ => new CilBodyElement.Instruction(instruction.Operation, instruction.Operand),
            };
            elements.Add(element with { Source = instruction.SourceOffset });
        }
        elements.Add(new CilBodyElement.Mark(labels[end]));
        var regions = body.ExceptionRegions
            .Select(region => new CilBodyRegion(
                region.Kind,
                labels[region.TryOffset],
                labels[region.TryOffset + region.TryLength],
                labels[region.HandlerOffset],
                labels[region.HandlerOffset + region.HandlerLength])
            {
                CatchType = region.CatchType,
                CatchTypeIdentity = region.CatchTypeIdentity,
                FilterStart = region.FilterOffset is { } filter ? labels[filter] : null,
                PassThroughLocal = region.PassThroughLocal is { } local ? locals[local] : null,
            })
            .ToImmutableArray();
        return new(new(locals, elements.ToImmutable(), regions) { InitializesLocals = body.InitializesLocals }, labels);
    }
}
