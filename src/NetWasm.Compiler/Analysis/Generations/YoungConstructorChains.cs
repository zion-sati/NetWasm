// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Analysis.NullChecks;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Generations;

// The constructor a class constructor chains to with its own this before anything in it can collect: the base or
// this constructor call reached from its start through instructions that neither call, allocate, touch a static
// (whose initialization may allocate) nor branch. A constructor with exception regions, or one that stores to this
// or takes its address, chains to none: its this may not be the object it started with.
internal static class YoungConstructorChains
{
    public static IEnumerable<(int Index, MethodInstanceModel Target)> Of(MethodInstanceModel method, CilMethodBody body)
    {
        if (!ConstructorReceivers.IsInstanceConstructor(method) || !body.ExceptionRegions.IsEmpty || WritesThis(body))
            yield break;
        // Whether each value on the stack is this.
        var stack = new List<bool>();
        // Validated IL ends in a control transfer: a branch or call this walk stops at, or a ret, throw, br or leave,
        // whose stack effect it does not follow; so the walk ends inside the body.
        for (var index = 0; ; index++)
        {
            var instruction = body.Instructions[index];
            switch (instruction.Operation)
            {
                case CilOperation.LoadArgument when ((CilOperand.Index)instruction.Operand!).Value == 0:
                    stack.Add(true);
                    continue;
                case CilOperation.Duplicate when stack.Count != 0:
                    stack.Add(stack[^1]);
                    continue;
                case CilOperation.Call when instruction.Operand is CilOperand.MethodInstance { Value: var target } &&
                    ConstructorReceivers.IsInstanceConstructor(target):
                    var receiver = stack.Count - target.Signature.ParameterTypes.Length - 1;
                    if (receiver >= 0 && stack[receiver]) yield return (index, target);
                    yield break;
                case CilOperation.BranchIfTrue or CilOperation.BranchIfFalse or CilOperation.Box or
                    CilOperation.NewArray or CilOperation.LoadStaticField or CilOperation.LoadStaticFieldAddress or
                    CilOperation.StoreStaticField:
                    yield break;
            }
            if (CilStackEffects.Of(instruction) is not { } effect || effect.Pops > stack.Count) yield break;
            stack.RemoveRange(stack.Count - effect.Pops, effect.Pops);
            for (var push = 0; push < effect.Pushes; push++) stack.Add(false);
        }
    }

    // Whether the body stores to its this argument or takes its address.
    public static bool WritesThis(CilMethodBody body) => body.Instructions.Any(instruction =>
        instruction.Operation is CilOperation.StoreArgument or CilOperation.LoadArgumentAddress &&
        ((CilOperand.Index)instruction.Operand!).Value == 0);
}
