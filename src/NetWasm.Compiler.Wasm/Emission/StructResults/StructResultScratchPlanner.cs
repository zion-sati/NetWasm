// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;

namespace NetWasm.Compiler.Wasm.Emission.StructResults;

// The method's own return and each managed call's (call, callvirt, managed calli) are the shapes it moves; a local of a
// kind serves every shape in turn, so the need per kind is the most any one shape has.
internal sealed class StructResultScratchPlanner(IStructResultClassifier classifier) : IStructResultScratchPlanner
{
    private readonly IStructResultClassifier _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));

    public ImmutableArray<CliValueKind> Plan(StructuredMethodHeader header, MemberExecutionPlan? memberExecution = null)
    {
        ArgumentNullException.ThrowIfNull(header);
        return Plan(header.Instructions, memberExecution, (header.MethodInstance?.Signature ?? header.Method.Signature).ReturnSignatureType);
    }

    public ImmutableArray<CliValueKind> Plan(IEnumerable<CilInstruction> instructions, MemberExecutionPlan? memberExecution = null)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        return Plan(instructions, memberExecution, null);
    }

    private ImmutableArray<CliValueKind> Plan(IEnumerable<CilInstruction> instructions, MemberExecutionPlan? memberExecution,
        CliTypeIdentity? returned)
    {
        var needs = new Dictionary<CliValueKind, int>();
        if (returned is not null) Add(returned);
        foreach (var instruction in instructions)
        {
            switch (instruction.Operation, instruction.Operand)
            {
                case (CilOperation.Call or CilOperation.CallVirtual, CilOperand.MethodInstance { Value: var method }):
                    Add(method.Signature.ReturnSignatureType);
                    AddExecuted(method.Definition.Key);
                    break;
                case (CilOperation.Call or CilOperation.CallVirtual, CilOperand.Entity { Key: var key }):
                    AddExecuted(key);
                    break;
                case (CilOperation.CallIndirect, CilOperand.CallSite { Unmanaged: false, Signature: var signature }):
                    Add(signature.ReturnSignatureType);
                    break;
            }
        }
        return [.. needs.OrderBy(static pair => pair.Key).SelectMany(static pair => Enumerable.Repeat(pair.Key, pair.Value))];

        void AddExecuted(EntityKey called)
        {
            if (memberExecution is null) return;
            if (memberExecution.MethodInvokers.Contains(called))
            {
                foreach (var executed in memberExecution.Methods.Values)
                {
                    Add(executed.Descriptor.Signature.ReturnSignatureType);
                    foreach (var target in executed.Targets) Add(target.Method.Signature.ReturnSignatureType);
                }
            }
            if (memberExecution.DelegateInvocation is { } invocation && invocation.Invokers.Contains(called))
            {
                foreach (var invoke in invocation.Methods.Values) Add(invoke.Signature.ReturnSignatureType);
            }
        }
        void Add(CliTypeIdentity type)
        {
            if (_classifier.Classify(type) is not { } shape) return;
            foreach (var group in shape.Fields.GroupBy(static field => field.Kind))
            {
                needs[group.Key] = Math.Max(needs.GetValueOrDefault(group.Key), group.Count());
            }
        }
    }
}
