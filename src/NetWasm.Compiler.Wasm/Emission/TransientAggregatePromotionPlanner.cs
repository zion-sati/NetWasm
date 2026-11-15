// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed class TransientAggregatePromotionPlanner(
    IMethodRepository methods,
    IRuntimeIntrinsicRegistry intrinsics,
    IValueLayoutProvider values,
    IInstanceFieldLayoutProvider fields) : ITransientAggregatePromotionPlanner
{
    private readonly IMethodRepository _methods =
        methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly IRuntimeIntrinsicRegistry _intrinsics =
        intrinsics ?? throw new ArgumentNullException(nameof(intrinsics));
    private readonly IValueLayoutProvider _values =
        values ?? throw new ArgumentNullException(nameof(values));
    private readonly IInstanceFieldLayoutProvider _fields =
        fields ?? throw new ArgumentNullException(nameof(fields));

    public TransientAggregatePromotionPlan Create(StructuredMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (!method.ExceptionGroups.IsEmpty)
        {
            return TransientAggregatePromotionPlan.Empty;
        }

        var producers = ImmutableDictionary.CreateBuilder<int, CliTypeIdentity>();
        var projections = ImmutableDictionary.CreateBuilder<int, TransientAggregateProjection>();
        var transfers = ImmutableHashSet.CreateBuilder<int>();
        var promotedLocals = ImmutableHashSet.CreateBuilder<int>();
        var reachableOffsets = method.InstructionEntryStacks.Keys.ToImmutableHashSet();

        foreach (var block in method.Blocks.Values.OrderBy(block => block.StartOffset))
        {
            var instructions = block.Instructions
                .Where(instruction => reachableOffsets.Contains(instruction.Offset))
                .ToImmutableArray();
            for (var index = 0; index < instructions.Length; index++)
            {
                var producer = instructions[index];
                if (!TryGetPromotableElement(producer, out var tuple, out var element))
                {
                    continue;
                }

                if (TryPlanDirectProjection(
                        instructions,
                        index,
                        producer,
                        tuple,
                        element,
                        producers,
                        projections))
                {
                    continue;
                }

                TryPlanLocalProjection(
                    method,
                    block,
                    instructions,
                    index,
                    producer,
                    tuple,
                    element,
                    producers,
                    projections,
                    transfers,
                    promotedLocals);
            }
        }

        return producers.Count == 0
            ? TransientAggregatePromotionPlan.Empty
            : new(
                producers.ToImmutable(),
                projections.ToImmutable(),
                transfers.ToImmutable(),
                promotedLocals.ToImmutable());
    }

    private bool TryGetPromotableElement(
        CilInstruction instruction,
        out CliTypeIdentity tuple,
        out CliTypeIdentity element)
    {
        tuple = null!;
        element = null!;
        if (instruction.Operation is not (CilOperation.Call or CilOperation.CallVirtual))
        {
            return false;
        }

        var method = instruction.Operand switch
        {
            CilOperand.MethodInstance instance => instance.Value.Definition,
            CilOperand.Entity entity => _methods.GetMethod(entity.Key),
            _ => null,
        };
        if (method is null ||
            !_intrinsics.TryGetIntrinsic(method.Key, out var intrinsic) ||
            intrinsic != RuntimeIntrinsic.IntegerDivRem)
        {
            return false;
        }

        var signature = instruction.Operand is CilOperand.MethodInstance instanceOperand
            ? instanceOperand.Value.Signature
            : method.Signature;
        // Only constructed generic identities carry type arguments; their factory
        // always supplies the generic definition as ElementType.
        var tupleDefinition = signature.ReturnSignatureType.ElementType!;
        if (signature.ParameterSignatureTypes is not [var left, var right] ||
            !left.Equals(right) ||
            signature.ReturnSignatureType.TypeArguments is not [var first, var second] ||
            !first.Equals(left) ||
            !second.Equals(left) ||
            tupleDefinition.FullName != "System.ValueTuple`2" ||
            tupleDefinition.Assembly!.Value != method.Key.Assembly ||
            left.CanonicalName is not (
                "primitive:i4" or "primitive:u4" or
                "primitive:i8" or "primitive:u8" or
                "primitive:nativeint" or "primitive:nativeuint"))
        {
            return false;
        }

        var elementLayout = _values.GetValueLayout(left);
        var layout = _values.GetValueLayout(signature.ReturnSignatureType);
        if (!layout.ReferenceOffsets.IsEmpty || !layout.ByReferenceOffsets.IsEmpty)
        {
            return false;
        }
        if (layout.Size != elementLayout.Size * 2)
        {
            return false;
        }

        element = left;
        tuple = signature.ReturnSignatureType;
        return true;
    }

    private bool TryPlanDirectProjection(
        ImmutableArray<CilInstruction> instructions,
        int index,
        CilInstruction producer,
        CliTypeIdentity tuple,
        CliTypeIdentity element,
        ImmutableDictionary<int, CliTypeIdentity>.Builder producers,
        ImmutableDictionary<int, TransientAggregateProjection>.Builder projections)
    {
        if (index + 4 >= instructions.Length ||
            instructions[index + 1].Operation != CilOperation.Duplicate ||
            !TryGetElementIndex(instructions[index + 2], tuple, element, out var first) ||
            instructions[index + 3].Operation != CilOperation.StoreLocal ||
            !TryGetElementIndex(instructions[index + 4], tuple, element, out var second) ||
            first == second)
        {
            return false;
        }

        producers.Add(producer.Offset, element);
        projections.Add(
            instructions[index + 2].Offset,
            new(producer.Offset, first));
        projections.Add(
            instructions[index + 4].Offset,
            new(producer.Offset, second));
        return true;
    }

    private bool TryPlanLocalProjection(
        StructuredMethod method,
        StructuredBlockDefinition producerBlock,
        ImmutableArray<CilInstruction> producerInstructions,
        int producerIndex,
        CilInstruction producer,
        CliTypeIdentity tuple,
        CliTypeIdentity element,
        ImmutableDictionary<int, CliTypeIdentity>.Builder producers,
        ImmutableDictionary<int, TransientAggregateProjection>.Builder projections,
        ImmutableHashSet<int>.Builder transfers,
        ImmutableHashSet<int>.Builder promotedLocals)
    {
        if (producerIndex + 1 >= producerInstructions.Length ||
            producerInstructions[producerIndex + 1].Operation != CilOperation.StoreLocal)
        {
            return false;
        }

        var store = producerInstructions[producerIndex + 1];
        var localIndex = CilOperandReader.GetIndex(store);
        if ((uint)localIndex >= (uint)method.Header.LocalSignatureTypes.Length ||
            !method.Header.LocalSignatureTypes[localIndex].Equals(tuple))
        {
            return false;
        }

        var localTransfers = new List<int> { store.Offset };
        var localProjections = new List<(int Offset, int ElementIndex)>();
        foreach (var block in method.Blocks.Values)
        {
            var instructions = block.Instructions;
            for (var index = 0; index < instructions.Length; index++)
            {
                var instruction = instructions[index];
                if (instruction.Operation is not (
                        CilOperation.LoadLocal or
                        CilOperation.StoreLocal or
                        CilOperation.LoadLocalAddress) ||
                    CilOperandReader.GetIndex(instruction) != localIndex)
                {
                    continue;
                }

                if (instruction.Offset == store.Offset)
                {
                    continue;
                }
                if (block.Id != producerBlock.Id ||
                    instruction.Operation is not (
                        CilOperation.LoadLocal or CilOperation.LoadLocalAddress) ||
                    index + 1 >= instructions.Length ||
                    !TryGetElementIndex(
                        instructions[index + 1],
                        tuple,
                        element,
                        out var elementIndex))
                {
                    return false;
                }

                localTransfers.Add(instruction.Offset);
                localProjections.Add((instructions[index + 1].Offset, elementIndex));
            }
        }

        if (localProjections.Select(projection => projection.ElementIndex)
            .ToImmutableHashSet().Count != 2)
        {
            return false;
        }

        producers.Add(producer.Offset, element);
        foreach (var offset in localTransfers)
        {
            transfers.Add(offset);
        }
        foreach (var projection in localProjections)
        {
            projections.Add(
                projection.Offset,
                new(producer.Offset, projection.ElementIndex));
        }
        promotedLocals.Add(localIndex);
        return true;
    }

    private bool TryGetElementIndex(
        CilInstruction instruction,
        CliTypeIdentity tuple,
        CliTypeIdentity element,
        out int elementIndex)
    {
        elementIndex = 0;
        if (instruction.Operation != CilOperation.LoadField ||
            instruction.Operand is not CilOperand.FieldInstance field)
        {
            return false;
        }

        if (!field.Value.DeclaringType.Equals(tuple))
        {
            return false;
        }

        elementIndex = field.Value.Definition.Name switch
        {
            "Item1" => 0,
            "Item2" => 1,
            _ => -1,
        };
        if (elementIndex < 0 || !field.Value.FieldType.Equals(element))
        {
            return false;
        }

        var elementLayout = _values.GetValueLayout(element);
        var fieldLayout = _fields.GetFieldLayout(field.Value);
        return fieldLayout.Offset == elementIndex * elementLayout.Size &&
            fieldLayout.Size == elementLayout.Size &&
            (fieldLayout.Type is null || fieldLayout.Type.Equals(element));
    }
}
