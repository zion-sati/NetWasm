// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Analysis.Attributes;

// Composite builder: the query's arguments go to locals, a null target throws as the call would, and the target is
// compared with each case's static Type object (typeof, which neither allocates nor throws) in turn. The first match
// runs that case's ordinary query fragment over the reloaded arguments; no match runs Otherwise's.
internal sealed class RuntimeTypeAttributeDispatchBuilder(IAttributeQueryCilBuilder fragments) :
    IRuntimeTypeAttributeDispatchBuilder
{
    private readonly IAttributeQueryCilBuilder _fragments = fragments ?? throw new ArgumentNullException(nameof(fragments));

    public AttributeCilFragment Build(RuntimeTypeAttributeDispatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegative(request.FirstLocal);
        if (request.ArgumentTypes.Length != request.Call.ArgumentCount)
        {
            throw new ArgumentException("Every query argument needs a local type.", nameof(request));
        }
        var instructions = ImmutableArray.CreateBuilder<CilInstruction>();
        var locals = ImmutableArray.CreateBuilder<CliTypeIdentity>();
        locals.AddRange(request.ArgumentTypes);
        var arguments = request.Call.ArgumentCount;
        var maxStack = arguments;
        for (var index = arguments - 1; index >= 0; index--)
        {
            Emit(CilOperation.StoreLocal, new CilOperand.Index(request.FirstLocal + index));
        }
        Emit(CilOperation.LoadLocal, new CilOperand.Index(request.FirstLocal));
        var toTarget = Emit(CilOperation.BranchIfTrue, new CilOperand.BranchTarget(0));
        if (request.NullArgumentName is { } name)
        {
            Emit(CilOperation.LoadString, new CilOperand.UserString(name));
        }
        Emit(CilOperation.NewObject, new CilOperand.MethodInstance(request.NullTargetException));
        Emit(CilOperation.Throw, new CilOperand.None());
        Target(toTarget);
        var toEnd = ImmutableArray.CreateBuilder<int>();
        foreach (var dispatchCase in request.Cases)
        {
            Emit(CilOperation.LoadLocal, new CilOperand.Index(request.FirstLocal));
            Emit(CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(dispatchCase.Type));
            Emit(CilOperation.MaterializeType, new CilOperand.TypeIdentity(dispatchCase.Type));
            Emit(CilOperation.CompareEqual, new CilOperand.None());
            var toNext = Emit(CilOperation.BranchIfFalse, new CilOperand.BranchTarget(0));
            Answer(dispatchCase.Selection);
            toEnd.Add(Emit(CilOperation.Branch, new CilOperand.BranchTarget(0)));
            Target(toNext);
        }
        Answer(request.Otherwise);
        foreach (var branch in toEnd)
        {
            Target(branch);
        }
        Emit(CilOperation.Nop, new CilOperand.None());
        return new(instructions.ToImmutable(), locals.ToImmutable(), maxStack);

        void Answer(AttributeQueryEntry selection)
        {
            for (var index = 0; index < arguments; index++)
            {
                Emit(CilOperation.LoadLocal, new CilOperand.Index(request.FirstLocal + index));
            }
            var fragment = _fragments.Build(new(request.Call.Operation, selection, arguments,
                request.Call.InheritArgument, request.InheritConstant, request.ResultArrayType,
                request.AmbiguityConstructor, request.FirstLocal + locals.Count));
            var start = instructions.Count;
            foreach (var instruction in fragment.Instructions)
            {
                var offset = instructions.Count;
                instructions.Add(instruction with
                {
                    Offset = offset,
                    NextOffset = offset + 1,
                    Operand = instruction.Operand is CilOperand.BranchTarget target
                        ? new CilOperand.BranchTarget(start + target.Offset)
                        : instruction.Operand,
                });
            }
            locals.AddRange(fragment.LocalTypes);
            // The fragment starts from the reloaded arguments, which it consumes.
            maxStack = Math.Max(maxStack, fragment.MaxStack);
        }

        int Emit(CilOperation operation, CilOperand operand)
        {
            var offset = instructions.Count;
            instructions.Add(new(offset, offset + 1, operation, operand));
            // Two values at most are live here: the target and its comparand (a token, then its Type).
            maxStack = Math.Max(maxStack, 2);
            return offset;
        }

        void Target(int branch) => instructions[branch] =
            instructions[branch] with { Operand = new CilOperand.BranchTarget(instructions.Count) };
    }
}
