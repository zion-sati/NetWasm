// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Analysis.Attributes;

// Composite builder: the query's arguments go to locals, a null target throws as the call would, and the target is
// compared with each case's static Type object (typeof, which neither allocates nor throws) in turn. The first match
// runs that case's ordinary query fragment over the reloaded arguments; no match runs Otherwise's. The cases share
// their locals by type (SharedLocals).
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
        var shared = new SharedLocals(request.FirstLocal, locals);
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
            var fragmentFirst = request.FirstLocal + locals.Count;
            var fragment = _fragments.Build(new(request.Call.Operation, selection, arguments,
                request.Call.InheritArgument, request.InheritConstant, request.ResultArrayType,
                request.AmbiguityConstructor, fragmentFirst));
            var start = instructions.Count;
            foreach (var instruction in shared.Assign(fragment, fragmentFirst))
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

    // A query runs one case's fragment, so a case can use the locals an earlier case used, by type, where it writes
    // each one before reading it. Otherwise every case adds locals of its own, and each reference local is a root slot
    // of its own that the method's safepoints clear: a query over n run-time types compiled to code quadratic in n at
    // O1 and up (5,000 types made Main a 167 MB function). A local is written before it is read when its first use is a
    // store and no branch lands between that store and its last use, so every path to a read passes the store; one
    // whose address is taken, or that a switch could reach, keeps a local of its own.
    private sealed class SharedLocals(int firstLocal, ImmutableArray<CliTypeIdentity>.Builder locals)
    {
        private readonly Dictionary<CliTypeIdentity, List<int>> _byType = [];

        // The fragment's instructions with its locals (numbered from fragmentFirst) renumbered to the shared ones.
        internal IEnumerable<CilInstruction> Assign(AttributeCilFragment fragment, int fragmentFirst)
        {
            var reusable = WrittenBeforeRead(fragment, fragmentFirst);
            var used = new Dictionary<CliTypeIdentity, int>();
            var map = new int[fragment.LocalTypes.Length];
            for (var index = 0; index < map.Length; index++)
            {
                var type = fragment.LocalTypes[index];
                if (!reusable[index])
                {
                    map[index] = Add(type);
                    continue;
                }
                if (!_byType.TryGetValue(type, out var slots)) _byType.Add(type, slots = []);
                var nth = used.GetValueOrDefault(type);
                used[type] = nth + 1;
                if (nth == slots.Count) slots.Add(Add(type));
                map[index] = slots[nth];
            }
            return fragment.Instructions.Select(instruction =>
                Local(instruction, fragmentFirst, map.Length) is { } local
                    ? instruction with { Operand = new CilOperand.Index(map[local]) }
                    : instruction);
        }

        private int Add(CliTypeIdentity type)
        {
            var index = firstLocal + locals.Count;
            locals.Add(type);
            return index;
        }

        private static bool[] WrittenBeforeRead(AttributeCilFragment fragment, int fragmentFirst)
        {
            var count = fragment.LocalTypes.Length;
            var reusable = new bool[count];
            if (fragment.Instructions.Any(instruction => instruction.Operation == CilOperation.Switch))
            {
                return reusable;
            }
            var targets = fragment.Instructions
                .Select(instruction => instruction.Operand is CilOperand.BranchTarget target ? target.Offset : -1)
                .Where(offset => offset >= 0)
                .ToHashSet();
            var first = new int[count];
            var last = new int[count];
            Array.Fill(first, -1);
            Array.Fill(reusable, true);
            for (var position = 0; position < fragment.Instructions.Length; position++)
            {
                var instruction = fragment.Instructions[position];
                if (Local(instruction, fragmentFirst, count) is not { } local)
                {
                    continue;
                }
                if (first[local] < 0)
                {
                    first[local] = position;
                    reusable[local] = instruction.Operation == CilOperation.StoreLocal;
                }
                reusable[local] &= instruction.Operation != CilOperation.LoadLocalAddress;
                last[local] = position;
            }
            for (var local = 0; local < count; local++)
            {
                for (var position = first[local] + 1; reusable[local] && first[local] >= 0 && position <= last[local]; position++)
                {
                    reusable[local] = !targets.Contains(fragment.Instructions[position].Offset);
                }
            }
            return reusable;
        }

        // The fragment's own local an instruction loads, stores or takes the address of, by its index in the fragment.
        private static int? Local(CilInstruction instruction, int fragmentFirst, int count) =>
            instruction.Operation is CilOperation.LoadLocal or CilOperation.StoreLocal or CilOperation.LoadLocalAddress &&
            instruction.Operand is CilOperand.Index { Value: var index } && index >= fragmentFirst && index < fragmentFirst + count
                ? index - fragmentFirst
                : null;
    }
}
