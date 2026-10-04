using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Analysis.Attributes;

internal sealed class AttributeQueryCilBuilder(IAttributeConstructionCilBuilder constructions) :
    IAttributeQueryCilBuilder
{
    private readonly IAttributeConstructionCilBuilder _constructions = constructions ??
        throw new ArgumentNullException(nameof(constructions));

    public AttributeCilFragment Build(AttributeQueryCilRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Selection);
        ArgumentNullException.ThrowIfNull(request.ResultArrayType);
        ArgumentOutOfRangeException.ThrowIfNegative(request.FirstLocal);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.ArgumentCount, 1);
        if (!Enum.IsDefined(request.Operation))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Unknown attribute query operation.");
        }
        if (!ValidResult(request.Selection.Direct) || !ValidResult(request.Selection.Inherited))
        {
            throw new ArgumentException("Attribute result kind must match the query operation.", nameof(request));
        }
        if (request.ResultArrayType.Shape != CliTypeShape.SzArray)
        {
            throw new ArgumentException("Attribute results require a vector type.", nameof(request));
        }
        if (request.InheritArgument is { } argument &&
            (argument < 0 || argument >= request.ArgumentCount) ||
            request.InheritConstant is null && request.InheritArgument is null)
        {
            throw new ArgumentException("An inheritance choice must be constant or identify an argument.",
                nameof(request));
        }
        if (request.Operation == AttributeQueryOperation.GetOne &&
            (request.InheritConstant != true && request.Selection.Direct is AttributeRetrievalResult { Attributes.Length: > 1 } ||
             request.InheritConstant != false && request.Selection.Inherited is AttributeRetrievalResult { Attributes.Length: > 1 }) &&
            request.AmbiguityConstructor is null)
        {
            throw new ArgumentException("An ambiguous singular query requires its exception constructor.",
                nameof(request));
        }

        var instructions = ImmutableArray.CreateBuilder<CilInstruction>();
        var locals = ImmutableArray.CreateBuilder<CliTypeIdentity>();
        var stack = request.ArgumentCount;
        var maxStack = stack;
        var inheritLocal = request.InheritConstant is null
            ? AddLocal(CliTypeIdentity.FromStackKind(CliValueKind.I4))
            : -1;
        for (var index = request.ArgumentCount - 1; index >= 0; index--)
        {
            if (inheritLocal >= 0 && index == request.InheritArgument)
            {
                Emit(CilOperation.StoreLocal, new CilOperand.Index(inheritLocal), 1, 0);
            }
            else
            {
                Emit(CilOperation.Pop, new CilOperand.None(), 1, 0);
            }
        }

        if (request.InheritConstant is { } inherit)
        {
            EmitResult(inherit ? request.Selection.Inherited : request.Selection.Direct);
        }
        else
        {
            Emit(CilOperation.LoadLocal, new CilOperand.Index(inheritLocal), 0, 1);
            var toInherited = Emit(CilOperation.BranchIfTrue, new CilOperand.BranchTarget(0), 1, 0);
            EmitResult(request.Selection.Direct);
            var toEnd = Emit(CilOperation.Branch, new CilOperand.BranchTarget(0), 0, 0);
            SetTarget(toInherited, instructions.Count);
            stack = 0;
            EmitResult(request.Selection.Inherited);
            SetTarget(toEnd, instructions.Count);
            Emit(CilOperation.Nop, new CilOperand.None(), 0, 0);
        }
        return new(instructions.ToImmutable(), locals.ToImmutable(), maxStack);

        bool ValidResult(AttributeQueryResult result) => request.Operation == AttributeQueryOperation.IsDefined
            ? result is AttributeExistenceResult : result is AttributeRetrievalResult;

        void EmitResult(AttributeQueryResult result)
        {
            if (result is AttributeExistenceResult presence)
            {
                Emit(CilOperation.LoadInt32, new CilOperand.ConstantI4(presence.IsDefined ? 1 : 0), 0, 1);
                return;
            }
            var attributes = ((AttributeRetrievalResult)result).Attributes;
            if (request.Operation == AttributeQueryOperation.GetOne)
            {
                if (attributes.IsEmpty)
                {
                    Emit(CilOperation.LoadNull, new CilOperand.None(), 0, 1);
                }
                else if (attributes.Length == 1)
                {
                    EmitAttribute(attributes[0]);
                }
                else
                {
                    EmitMany(attributes);
                    Emit(CilOperation.Pop, new CilOperand.None(), 1, 0);
                    Emit(CilOperation.NewObject,
                        new CilOperand.MethodInstance(request.AmbiguityConstructor!), 0, 1);
                    Emit(CilOperation.Throw, new CilOperand.None(), 1, 0);
                }
            }
            else
            {
                EmitMany(attributes);
            }
        }

        void EmitMany(ImmutableArray<AttributeConstructionPlan> attributes)
        {
            var array = AddLocal(request.ResultArrayType);
            var element = request.ResultArrayType.ElementType!;
            Emit(CilOperation.LoadInt32, new CilOperand.ConstantI4(attributes.Length), 0, 1);
            Emit(CilOperation.NewArray, new CilOperand.TypeIdentity(element), 1, 1);
            Emit(CilOperation.StoreLocal, new CilOperand.Index(array), 1, 0);
            for (var index = 0; index < attributes.Length; index++)
            {
                Emit(CilOperation.LoadLocal, new CilOperand.Index(array), 0, 1);
                Emit(CilOperation.LoadInt32, new CilOperand.ConstantI4(index), 0, 1);
                EmitAttribute(attributes[index]);
                Emit(CilOperation.StoreArrayElementReference, new CilOperand.None(), 3, 0);
            }
            Emit(CilOperation.LoadLocal, new CilOperand.Index(array), 0, 1);
        }

        void EmitAttribute(AttributeConstructionPlan attribute)
        {
            var fragment = _constructions.Build(attribute, request.FirstLocal + locals.Count);
            var start = instructions.Count;
            foreach (var instruction in fragment.Instructions)
            {
                instructions.Add(instruction with
                {
                    Offset = start + instruction.Offset,
                    NextOffset = start + instruction.NextOffset,
                });
            }
            locals.AddRange(fragment.LocalTypes);
            maxStack = Math.Max(maxStack, stack + fragment.MaxStack);
            stack++;
        }

        int AddLocal(CliTypeIdentity type)
        {
            var index = request.FirstLocal + locals.Count;
            locals.Add(type);
            return index;
        }

        int Emit(CilOperation operation, CilOperand operand, int consumed, int produced)
        {
            var offset = instructions.Count;
            instructions.Add(new(offset, offset + 1, operation, operand));
            stack += produced - consumed;
            maxStack = Math.Max(maxStack, stack);
            return offset;
        }

        void SetTarget(int instruction, int target) => instructions[instruction] =
            instructions[instruction] with { Operand = new CilOperand.BranchTarget(target) };
    }
}
