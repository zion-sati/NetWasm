// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.AddressTakenValues;

// A value constructed by newobj and stored whole through a local's address taken before the constructor's
// arguments, `ldloca x; <arguments>; newobj S::.ctor; stobj S`, as Roslyn assigns a new struct to an out parameter
// that inlining has bound to a local, becomes `<arguments>; newobj S::.ctor; stloc x`. Taking a local's address has
// no effect, and the store still follows the construction, so a constructor that throws leaves the local unchanged.
// Once the constructor is inlined, its branches would otherwise hold the address on the stack across blocks, where
// the scalar replacer cannot follow it, and the local would stay in memory.
internal static class ConstructedLocalStoreFolder
{
    public static CilMethodBody Rewrite(CilMethodBody body, IReadOnlyDictionary<int, ImmutableArray<CliValueKind>> entryStacks)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(entryStacks);
        var instructions = body.Instructions;
        StackSlotProducers? producers = null;
        ImmutableArray<CilInstruction>.Builder? rewritten = null;
        for (var index = 1; index < instructions.Length; index++)
        {
            if (instructions[index] is not { Operation: CilOperation.StoreObject, Operand: CilOperand.TypeIdentity { Value: var stored } } ||
                !stored.IsValueType ||
                instructions[index - 1] is not { Operation: CilOperation.NewObject, Operand: CilOperand.MethodInstance { Value: var constructor } } ||
                constructor.DeclaringType.CanonicalName != stored.CanonicalName ||
                !entryStacks.TryGetValue(instructions[index].Offset, out var stack) || stack.Length < 2)
            {
                continue;
            }
            producers ??= new StackSlotProducers(body, entryStacks);
            if (producers.Find(index, stack.Length - 2) is not { } producer ||
                instructions[producer] is not { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var local } } ||
                (uint)local >= (uint)body.LocalSignatureTypes.Length ||
                body.LocalSignatureTypes[local].CanonicalName != stored.CanonicalName)
            {
                continue;
            }
            rewritten ??= instructions.ToBuilder();
            rewritten[producer] = instructions[producer] with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
            rewritten[index] = instructions[index] with { Operation = CilOperation.StoreLocal, Operand = new CilOperand.Index(local) };
        }
        return rewritten is null ? body : body with { Instructions = rewritten.ToImmutable() };
    }
}
