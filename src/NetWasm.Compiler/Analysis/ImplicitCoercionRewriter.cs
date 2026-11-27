// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// ECMA-335 III.1.6 lets an argument, a store to a local, argument or field, and a
// return take an int32 where a native int is declared and the reverse, which C# relies
// on for a multidimensional array's indices and lengths of other integer types. Each
// such coercion the stack validator finds becomes an explicit conversion, sign-extending
// or truncating as desktop .NET does, so later rewrites and the emitters see matching
// kinds. ECMA-335 III.1.5's tables also combine operands of two kinds: an int32 with a
// native int in a comparison (the int32 sign-extends), a float32 with a float64 (both F;
// the float32 widens), and a shift count of native int (it narrows to the int32 every
// shift takes); those operands are converted the same way (arithmetic's emitters widen an
// int32 beside a native int themselves). The conversion
// follows the value's producer when the consumer's block pushed it, or else precedes the
// consumer: a call's later arguments wait in new locals of their parameters' types, an
// operation's of their stack kinds, while the value is converted.
internal sealed class ImplicitCoercionRewriter(IControlFlowGraphBuilder graphs, ITypedStackValidator stacks)
    : IMethodRewriteRule
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var validated = _stacks.Validate(_graphs.Build(body));
        if (validated.ImplicitCoercions.IsEmpty)
        {
            return body;
        }

        var producers = new StackSlotProducers(body, validated.InstructionEntryStacks);
        var indices = new Dictionary<int, int>();
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            indices.Add(body.Instructions[index].Offset, index);
        }
        var before = new List<CilInstruction>[body.Instructions.Length];
        var after = new List<CilInstruction>[body.Instructions.Length];
        var locals = new List<CliTypeIdentity>();
        foreach (var coercions in validated.ImplicitCoercions.GroupBy(static coercion => coercion.Offset))
        {
            var consumer = indices[coercions.Key];
            var unproduced = new Dictionary<int, CliValueKind>();
            foreach (var coercion in coercions)
            {
                if (producers.Find(consumer, coercion.StackIndex) is int producer)
                {
                    (after[producer] ??= []).Add(Conversion(coercion.To));
                }
                else
                {
                    unproduced.Add(coercion.StackIndex, coercion.To);
                }
            }
            if (unproduced.Count != 0)
            {
                before[consumer] = Convert(body, consumer, validated.InstructionEntryStacks[coercions.Key],
                    unproduced, locals);
            }
        }

        var replacements = new List<CilInstruction>[body.Instructions.Length];
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            replacements[index] = [.. before[index] ?? [], body.Instructions[index], .. after[index] ?? []];
        }
        return RenumberedInstructions.Splice(body, replacements) with
        {
            Locals = body.Locals.AddRange(locals.Select(static local => local.StackKind)),
            LocalSignatureTypes = body.LocalSignatureTypes.AddRange(locals),
        };
    }

    // The consumer's operands from the lowest coerced one up, converted where coerced:
    // those above it are stored in new locals and loaded again once it is converted.
    private static List<CilInstruction> Convert(
        CilMethodBody body,
        int consumer,
        ImmutableArray<CliValueKind> stack,
        Dictionary<int, CliValueKind> coercions,
        List<CliTypeIdentity> locals)
    {
        var depth = stack.Length;
        var lowest = coercions.Keys.Min();
        var sequence = new List<CilInstruction>();
        var held = new int[depth - lowest - 1];
        for (var slot = depth - 1; slot > lowest; slot--)
        {
            if (coercions.TryGetValue(slot, out var kind))
            {
                sequence.Add(Conversion(kind));
            }
            held[slot - lowest - 1] = body.Locals.Length + locals.Count;
            locals.Add(OperandType(body.Instructions[consumer], stack, slot));
            sequence.Add(Instruction(CilOperation.StoreLocal, new CilOperand.Index(held[slot - lowest - 1])));
        }
        sequence.Add(Conversion(coercions[lowest]));
        sequence.AddRange(held.Select(static local => Instruction(CilOperation.LoadLocal, new CilOperand.Index(local))));
        return sequence;
    }

    // The declared type of a call's operand above the lowest one coerced: a parameter, or
    // calli's function pointer on top; a numeric operand of an operation is its stack kind.
    private static CliTypeIdentity OperandType(CilInstruction consumer, ImmutableArray<CliValueKind> stack, int slot)
    {
        if (consumer.Operand is not (CilOperand.CallSite or CilOperand.MethodInstance))
        {
            return CliTypeIdentity.FromStackKind(stack[slot]);
        }
        var depth = stack.Length;
        var signature = consumer.Operand is CilOperand.CallSite site
            ? site.Signature
            : ((CilOperand.MethodInstance)consumer.Operand).Value.Signature;
        var parameter = slot - (depth - signature.ParameterTypes.Length -
            (consumer.Operation == CilOperation.CallIndirect ? 1 : 0));
        return parameter < signature.ParameterSignatureTypes.Length
            ? signature.ParameterSignatureTypes[parameter]
            : CliTypeIdentity.FromStackKind(CliValueKind.NativeInt);
    }

    private static CilInstruction Conversion(CliValueKind kind) => Instruction(
        kind switch
        {
            CliValueKind.I4 => CilOperation.ConvertInt32,
            CliValueKind.F8 => CilOperation.ConvertFloat64,
            _ => CilOperation.ConvertNativeInt,
        },
        new CilOperand.None());

    private static CilInstruction Instruction(CilOperation operation, CilOperand operand) => new(0, 0, operation, operand);
}
