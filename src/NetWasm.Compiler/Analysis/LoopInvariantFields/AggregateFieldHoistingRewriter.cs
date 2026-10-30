using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.LoopInvariantFields;

internal sealed class AggregateFieldHoistingRewriter(
    IAggregateFieldHoistingPlanner planner) : IMethodRewriteRule
{
    private readonly IAggregateFieldHoistingPlanner _planner = planner ??
        throw new ArgumentNullException(nameof(planner));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var plan = _planner.Plan(body) ??
            throw new InvalidOperationException("The aggregate field hoisting planner returned no plan.");
        if (plan.Hoists.IsEmpty)
        {
            return body;
        }

        var insertions = plan.Hoists
            .GroupBy(hoist => hoist.PreheaderTerminatorOffset)
            .ToImmutableDictionary(
                group => group.Key,
                group => group.OrderBy(hoist => hoist.CachedLocalIndex).ToImmutableArray());
        var addressReplacements = new Dictionary<int, int>();
        var projectionReplacements = new Dictionary<int, int>();
        var localTypes = body.LocalSignatureTypes.ToBuilder();
        foreach (var hoist in plan.Hoists.OrderBy(hoist => hoist.CachedLocalIndex))
        {
            if (hoist.CachedLocalIndex != localTypes.Count ||
                hoist.AddressOffsets.IsDefaultOrEmpty ||
                hoist.AddressOffsets.Length != hoist.ProjectionOffsets.Length)
            {
                throw new InvalidOperationException("The aggregate field hoisting plan is inconsistent.");
            }

            localTypes.Add(hoist.FieldType);
            for (var index = 0; index < hoist.AddressOffsets.Length; index++)
            {
                if (!addressReplacements.TryAdd(
                        hoist.AddressOffsets[index],
                        hoist.CachedLocalIndex) ||
                    !projectionReplacements.TryAdd(
                        hoist.ProjectionOffsets[index],
                        hoist.CachedLocalIndex))
                {
                    throw new InvalidOperationException(
                        "The aggregate field hoisting plan contains overlapping replacements.");
                }
            }
        }

        var starts = new Dictionary<int, int>();
        var instructionCount = 0;
        var insertionCount = 0;
        foreach (var instruction in body.Instructions)
        {
            starts.Add(instruction.Offset, instructionCount);
            if (insertions.TryGetValue(instruction.Offset, out var before))
            {
                instructionCount = checked(instructionCount + before.Length * 3);
                insertionCount++;
            }
            instructionCount = checked(instructionCount + 1);
        }
        if (insertionCount != insertions.Count)
        {
            throw new InvalidOperationException(
                "The aggregate field hoisting plan identifies an unknown preheader.");
        }
        starts.Add(body.Instructions[^1].NextOffset, instructionCount);

        var rewritten = ImmutableArray.CreateBuilder<CilInstruction>(instructionCount);
        foreach (var instruction in body.Instructions)
        {
            if (insertions.TryGetValue(instruction.Offset, out var before))
            {
                foreach (var hoist in before)
                {
                    Add(
                        CilOperation.LoadLocalAddress,
                        new CilOperand.Index(hoist.SourceLocalIndex),
                        hoist.AddressLoad.SourceOffset);
                    Add(
                        hoist.Projection.Operation,
                        hoist.Projection.Operand,
                        hoist.Projection.SourceOffset);
                    Add(
                        CilOperation.StoreLocal,
                        new CilOperand.Index(hoist.CachedLocalIndex),
                        hoist.Projection.SourceOffset);
                }
            }

            var operation = instruction.Operation;
            var operand = instruction.Operand;
            if (addressReplacements.TryGetValue(instruction.Offset, out var cachedLocal))
            {
                operation = CilOperation.LoadLocal;
                operand = new CilOperand.Index(cachedLocal);
            }
            else if (projectionReplacements.ContainsKey(instruction.Offset))
            {
                operation = CilOperation.Nop;
                operand = new CilOperand.None();
            }

            Add(operation, RenumberedInstructions.Operand(operand, starts), instruction.SourceOffset);
        }

        return body with
        {
            Instructions = rewritten.MoveToImmutable(),
            LocalSignatureTypes = localTypes.ToImmutable(),
            Locals = [.. localTypes.Select(type => type.StackKind)],
            MaxStack = Math.Max(body.MaxStack, 1),
            ExceptionRegions = [.. body.ExceptionRegions.Select(region =>
                RenumberedInstructions.Region(region, starts))],
        };

        void Add(CilOperation operation, CilOperand operand, int sourceOffset)
        {
            rewritten.Add(new CilInstruction(
                rewritten.Count,
                rewritten.Count + 1,
                operation,
                operand)
            {
                OriginalOffset = sourceOffset,
            });
        }
    }
}
