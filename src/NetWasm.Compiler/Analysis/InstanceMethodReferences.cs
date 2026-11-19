// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Analysis.NullChecks;

namespace NetWasm.Compiler.Analysis;

// The ways a program reaches an instance method of a reference type other than newobj, which whole-program proofs
// about the object a method receives judge: each naming instruction, and what delegates and member execution
// invoke.
internal static class InstanceMethodReferences
{
    // Each instruction that names an instance method of a reference type other than by newobj, by index, and the
    // method it names.
    public static IEnumerable<(int Index, MethodInstanceModel Target)> Of(CilMethodBody body,
        ICalledMethodResolver calledMethods)
    {
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var instruction = body.Instructions[index];
            var target = instruction.Operation switch
            {
                CilOperation.Call or CilOperation.CallVirtual or CilOperation.LoadFunction or
                    CilOperation.LoadVirtualFunction => calledMethods.Resolve(instruction),
                // A method handle reaches reflection, which can invoke the method on any object.
                CilOperation.LoadMethodToken => ((CilOperand.MethodInstance)instruction.Operand!).Value,
                _ => null,
            };
            if (target is not null && ConstructorReceivers.IsClassInstanceMethod(target))
            {
                yield return (index, target);
            }
        }
    }

    // The methods delegates bind to and member execution invokes, which may receive any object.
    public static IEnumerable<MethodInstanceModel> Invoked(ReachableProgram program)
    {
        foreach (var binding in program.DelegateBindings) yield return binding.Target;
        foreach (var plan in program.MemberExecution.Methods.Values)
        {
            yield return plan.Descriptor;
            foreach (var target in plan.Targets) yield return target.Method;
        }
        if (program.MemberExecution.DelegateInvocation is { } invocation)
        {
            foreach (var method in invocation.Methods.Values) yield return method;
        }
    }
}
