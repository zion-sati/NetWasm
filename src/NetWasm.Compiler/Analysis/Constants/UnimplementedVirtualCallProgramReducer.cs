// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

// A virtual or interface call whose dispatch site has no target in the whole program can only be reached
// with a null receiver, since no object the program allocates implements the method, so it always throws
// NullReferenceException: the call becomes a throw of its receiver (a throw of null raises that exception), its other
// arguments' pushes go, and the code after it is unreachable, which the specializer removes. MemoryMarshal.TryGetArray's
// MemoryManager<T> path, in a program that makes no MemoryManager<T>, then no longer takes a local's address, so a
// caller it is inlined into needs no frame for it. In a receiver flow audit build, a call whose bound admitted no
// target keeps its dispatch, so a receiver the bound missed fails the audit there instead of being thrown here.
internal sealed class UnimplementedVirtualCallProgramReducer(
    IDelegateTypeRecognizer delegates,
    IMethodSpecializer rewrites,
    bool auditsReceivers = false) : IReachableProgramReducer
{
    private readonly IDelegateTypeRecognizer _delegates = delegates ?? throw new ArgumentNullException(nameof(delegates));
    private readonly IMethodSpecializer _rewrites = rewrites ?? throw new ArgumentNullException(nameof(rewrites));

    public ImmutableDictionary<string, CilMethodBody> Reduce(ReachableProgram program, ReachabilityRoots roots)
    {
        ArgumentNullException.ThrowIfNull(program);
        var result = ImmutableDictionary.CreateBuilder<string, CilMethodBody>(StringComparer.Ordinal);
        foreach (var method in program.Methods.Values.Concat(program.ConstructedMethods.Values)
            .DistinctBy(body => body.Method.CanonicalName, StringComparer.Ordinal))
        {
            var instructions = method.Body.Instructions;
            List<CilInstruction>[]? replacements = null;
            HashSet<int>? targets = null;
            for (var index = 0; index < instructions.Length; index++)
            {
                if (instructions[index] is not
                    {
                        Operation: CilOperation.CallVirtual, Operand: CilOperand.MethodInstance { Value: var called },
                    } call ||
                    index > 0 && instructions[index - 1].Operation == CilOperation.Constrained ||
                    called.Definition.IsStatic || called.DeclaringType.IsValueType || _delegates.Recognize(called.DeclaringType) ||
                    !program.DispatchCallSites.TryGetValue($"{method.Method.CanonicalName}@{call.Offset:x8}", out var site) ||
                    site.Targets.IsDefault || !site.Targets.IsEmpty ||
                    // Targets the receiver flow chose are empty because nothing reached the call on the paths the flow
                    // follows, not because nothing implements the method: a receiver it missed fails loudly where the
                    // call dispatches (InvalidCastException), rather than being thrown here in place of an exception.
                    site.PreciseTargets || auditsReceivers && site.BoundedTargets)
                {
                    continue;
                }
                targets ??= BranchTargets(instructions);
                replacements ??= [.. instructions.Select(original => new List<CilInstruction> { original })];
                // The arguments above the receiver: pushes that only push, right before the call, go; others are popped.
                var arguments = called.Signature.ParameterTypes.Length;
                var removed = 0;
                while (removed < arguments && index - 1 - removed >= 0 &&
                    IsPush(instructions[index - 1 - removed].Operation) &&
                    !targets.Contains(instructions[index - 1 - removed].Offset) &&
                    !targets.Contains(instructions[index - removed].Offset))
                {
                    removed++;
                }
                if (removed != arguments) removed = 0;
                for (var argument = 1; argument <= removed; argument++)
                {
                    replacements[index - argument] = [Nop(instructions[index - argument])];
                }
                var replacement = new List<CilInstruction>();
                for (var pop = 0; pop < arguments - removed; pop++)
                {
                    replacement.Add(call with { Operation = CilOperation.Pop, Operand = new CilOperand.None() });
                }
                replacement.Add(call with { Operation = CilOperation.Throw, Operand = new CilOperand.None() });
                replacements[index] = replacement;
            }
            if (replacements is not null)
            {
                result.Add(method.Method.CanonicalName, _rewrites.Rewrite(RenumberedInstructions.Splice(method.Body, replacements)));
            }
        }
        return result.ToImmutable();
    }

    private static CilInstruction Nop(CilInstruction instruction) =>
        instruction with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };

    private static bool IsPush(CilOperation operation) => operation is CilOperation.LoadLocal or
        CilOperation.LoadLocalAddress or CilOperation.LoadArgument or CilOperation.LoadArgumentAddress or
        CilOperation.LoadInt32 or CilOperation.LoadInt64 or CilOperation.LoadFloat32 or CilOperation.LoadFloat64 or
        CilOperation.LoadNull or CilOperation.LoadString;

    private static HashSet<int> BranchTargets(ImmutableArray<CilInstruction> instructions)
    {
        var targets = new HashSet<int>();
        foreach (var instruction in instructions)
        {
            switch (instruction.Operand)
            {
                case CilOperand.BranchTarget branch:
                    targets.Add(branch.Offset);
                    break;
                case CilOperand.SwitchTargets cases:
                    targets.UnionWith(cases.Offsets);
                    break;
            }
        }
        return targets;
    }
}
