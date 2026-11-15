// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

// Bounded member execution allocates a constructed object, or the box for a struct result, before it calls the
// member.
public sealed class MemberExecutionAllocationCallClassifier(
    ITypeRepository types,
    IMethodRepository methods) : IAllocationTemporaryCallClassifier
{
    private readonly ITypeRepository _types = types ?? throw new ArgumentNullException(nameof(types));
    private readonly IMethodRepository _methods = methods ?? throw new ArgumentNullException(nameof(methods));

    public bool Classify(CilInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        if (instruction.Operation != CilOperation.Call)
        {
            return false;
        }
        var method = instruction.Operand switch
        {
            CilOperand.Entity entity => _methods.GetMethod(entity.Key),
            CilOperand.MethodInstance instance => instance.Value.Definition,
            _ => null,
        };
        return method is { IsStatic: true, Name: "InvokeMethod" } &&
            _types.GetTypeDefinition(method.DeclaringType).FullName ==
                "System.Runtime.CompilerServices.RuntimeMemberExecution";
    }
}
