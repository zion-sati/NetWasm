// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

// Lays a runtime-provided body out at consecutive offsets, numbers its declared locals in order and any other local
// where it is first used, and resolves its labels, so the compiler reads it like any decoded body.
internal sealed class CilBodyLayout : ICilBodyLayout
{
    public CilMethodBody Lay(CilBodyDraft draft, MethodInstanceModel method, int maxStack)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(method);
        var labels = new Dictionary<CilBodyLabel, int>(ReferenceEqualityComparer.Instance);
        var locals = new Dictionary<CilBodyLocal, int>(ReferenceEqualityComparer.Instance);
        var order = new List<CilBodyLocal>();
        foreach (var local in draft.Locals)
        {
            if (locals.TryAdd(local, order.Count))
            {
                order.Add(local);
            }
        }
        var offset = 0;
        foreach (var element in draft.Elements)
        {
            switch (element)
            {
                case CilBodyElement.Mark mark:
                    if (!labels.TryAdd(mark.Label, offset))
                    {
                        throw new InvalidOperationException("A label is marked once.");
                    }
                    break;
                case CilBodyElement.LocalInstruction { Local: var local }:
                    if (locals.TryAdd(local, order.Count))
                    {
                        order.Add(local);
                    }
                    offset++;
                    break;
                default:
                    offset++;
                    break;
            }
        }

        var instructions = ImmutableArray.CreateBuilder<CilInstruction>(offset);
        foreach (var element in draft.Elements)
        {
            (CilOperation Operation, CilOperand Operand)? instruction = element switch
            {
                CilBodyElement.Instruction plain => (plain.Operation, plain.Operand),
                CilBodyElement.LocalInstruction local => (local.Operation, new CilOperand.Index(locals[local.Local])),
                CilBodyElement.Branch branch => (branch.Operation, new CilOperand.BranchTarget(Resolve(labels, branch.Target))),
                CilBodyElement.Switch cases => (CilOperation.Switch,
                    new CilOperand.SwitchTargets([.. cases.Targets.Select(target => Resolve(labels, target))])),
                _ => null,
            };
            if (instruction is var (operation, operand))
            {
                instructions.Add(new(instructions.Count, instructions.Count + 1, operation, operand)
                {
                    OriginalOffset = element.Source,
                });
            }
        }
        var regions = draft.Regions
            .Select(region =>
            {
                var tryStart = Resolve(labels, region.TryStart);
                var handlerStart = Resolve(labels, region.HandlerStart);
                return new CilExceptionRegion(region.Kind, tryStart, Resolve(labels, region.TryEnd) - tryStart,
                    handlerStart, Resolve(labels, region.HandlerEnd) - handlerStart, region.CatchType,
                    region.FilterStart is { } filter ? Resolve(labels, filter) : null)
                {
                    CatchTypeIdentity = region.CatchTypeIdentity,
                    PassThroughLocal = region.PassThroughLocal is { } local ? locals[local] : null,
                };
            })
            .ToImmutableArray();
        return new(method.Definition, maxStack, [.. order.Select(local => local.Type.StackKind)],
            instructions.MoveToImmutable())
        {
            MethodInstance = method,
            LocalSignatureTypes = [.. order.Select(local => local.Type)],
            PinnedLocals = [.. order.Select((local, index) => (local, index)).Where(item => item.local.Pinned)
                .Select(item => item.index)],
            ExceptionRegions = regions,
            InitializesLocals = draft.InitializesLocals,
        };
    }

    private static int Resolve(Dictionary<CilBodyLabel, int> labels, CilBodyLabel label) =>
        labels.TryGetValue(label, out var offset)
            ? offset
            : throw new InvalidOperationException("A branch targets a label that is never marked.");
}
