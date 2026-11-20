// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops;

internal interface ILoopArgumentLocalizer
{
    // The body with each argument a loop reads, and the method only reads, in a local of its own.
    CilMethodBody Rewrite(CilMethodBody body);
}

// The loop passes follow locals: a counted loop's index, its bound, the array it walks and the offsets it reads at
// are locals the loop leaves alone. An argument the method only reads is as fixed as such a local, and an inlined
// method already has its arguments in locals. So each int or array argument that a loop reads, and that the method
// neither writes nor takes the address of, is copied into a new local on entry and read from it: a loop bounded by
// `count`, walking `values` or reading at `start + index` is then the same loop whether its method was inlined or
// called.
internal sealed class LoopArgumentLocalizer(bool enabled) : ILoopArgumentLocalizer
{
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.FromStackKind(CliValueKind.I4);

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled) return body;
        var arguments = LoopArguments(body);
        if (arguments.Length == 0) return body;
        var locals = arguments
            .Select((argument, index) => KeyValuePair.Create(argument.Argument, body.Locals.Length + index))
            .ToDictionary();
        var replacements = body.Instructions
            .Select(instruction => new List<CilInstruction>
            {
                instruction is { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: var argument } } &&
                    locals.TryGetValue(argument, out var local)
                    ? instruction with { Operation = CilOperation.LoadLocal, Operand = new CilOperand.Index(local) }
                    : instruction,
            })
            .ToArray();
        var entry = arguments.SelectMany(argument => new[]
        {
            Synthetic(CilOperation.LoadArgument, argument.Argument),
            Synthetic(CilOperation.StoreLocal, locals[argument.Argument]),
        });
        return RenumberedInstructions.Splice(body, replacements, [.. entry]) with
        {
            MaxStack = Math.Max(1, body.MaxStack),
            Locals = body.Locals.AddRange(arguments.Select(argument => argument.Type.StackKind)),
            LocalSignatureTypes = body.LocalSignatureTypes.AddRange(arguments.Select(argument => argument.Type)),
        };
    }

    // The int and array arguments read between a backward branch and its target, which nothing stores to or takes
    // the address of, in order.
    private static ImmutableArray<(int Argument, CliTypeIdentity Type)> LoopArguments(CilMethodBody body)
    {
        var loops = body.Instructions
            .Where(instruction => instruction.Operand is CilOperand.BranchTarget target && target.Offset <= instruction.Offset)
            .Select(instruction => (Start: ((CilOperand.BranchTarget)instruction.Operand).Offset, End: instruction.Offset))
            .ToArray();
        if (loops.Length == 0) return [];
        var parameters = (body.MethodInstance?.Signature ?? body.Method.Signature).ParameterSignatureTypes;
        var first = body.Method.IsStatic ? 0 : 1;
        var changed = body.Instructions
            .Where(instruction => instruction.Operation is CilOperation.StoreArgument or CilOperation.LoadArgumentAddress)
            .Select(instruction => ((CilOperand.Index)instruction.Operand).Value)
            .ToHashSet();
        return
        [
            .. body.Instructions
                .Where(instruction => instruction.Operation == CilOperation.LoadArgument &&
                    loops.Any(loop => loop.Start <= instruction.Offset && instruction.Offset <= loop.End))
                .Select(instruction => ((CilOperand.Index)instruction.Operand).Value)
                .Where(argument => argument >= first && argument - first < parameters.Length && !changed.Contains(argument))
                .Distinct()
                .Order()
                .Select(argument => (argument, parameters[argument - first]))
                .Where(argument => argument.Item2.Equals(Int32) || argument.Item2.Shape == CliTypeShape.SzArray),
        ];
    }

    private static CilInstruction Synthetic(CilOperation operation, int index) =>
        new(0, 1, operation, new CilOperand.Index(index));
}
