// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

internal sealed class CallArgumentConstantCollector(ICalledMethodResolver calls) : ICallArgumentConstantCollector
{
    private readonly ICalledMethodResolver _calls = calls ?? throw new ArgumentNullException(nameof(calls));

    public ImmutableArray<ConstantCallEvaluation> Collect(ManagedMethodBody method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var result = ImmutableArray.CreateBuilder<ConstantCallEvaluation>();
        // A parameter the method stores to or takes the address of may not hold its caller's value when loaded.
        var receiver = method.Method.Definition.IsStatic ? 0 : 1;
        var changed = method.Body.Instructions
            .Where(static instruction => instruction.Operation is CilOperation.StoreArgument or CilOperation.LoadArgumentAddress)
            .Select(static instruction => ((CilOperand.Index)instruction.Operand).Value)
            .ToHashSet();
        foreach (var block in method.ControlFlow.Graph.Blocks)
        {
            var stack = new List<ScalarConstant?>();
            // Beside each entry, the declared parameter it holds the unchanged value of, or -1.
            var forwarded = new List<int>();
            var unmodeled = false;
            foreach (var instruction in block.Instructions)
            {
                if (!method.ControlFlow.InstructionEntryStacks.TryGetValue(instruction.Offset, out var entry)) continue;
                if (unmodeled)
                {
                    // An unmodeled instruction pops from the top and pushes at most one value (dup is modeled), so
                    // the entries below what it touched keep their facts and the top is unknown.
                    var kept = entry.Length == 0 ? 0 : Math.Min(stack.Count, entry.Length - 1);
                    stack.RemoveRange(kept, stack.Count - kept);
                    forwarded.RemoveRange(kept, forwarded.Count - kept);
                    while (stack.Count < entry.Length) Push(null);
                    unmodeled = false;
                }
                else if (stack.Count != entry.Length)
                {
                    stack.Clear();
                    forwarded.Clear();
                    for (var index = 0; index < entry.Length; index++) Push(null);
                }
                // Static calls, direct calls to instance methods (constructor chaining and base calls included),
                // callvirt to non-virtual methods (C#'s null-checked instance calls) and newobj. The recorded
                // arguments are the declared parameters, without the receiver.
                if (instruction.Operation is CilOperation.Call or CilOperation.CallVirtual or CilOperation.NewObject &&
                    _calls.Resolve(instruction) is { } target &&
                    (instruction.Operation != CilOperation.CallVirtual || !target.Definition.IsVirtual) &&
                    (instruction.Operation != CilOperation.NewObject || !target.Definition.IsStatic))
                {
                    var count = target.Signature.ParameterTypes.Length;
                    if (count > stack.Count) throw new InvalidOperationException("Validated call stack is shorter than its signature.");
                    var parameters = forwarded.GetRange(forwarded.Count - count, count);
                    result.Add(new(target, [.. stack.GetRange(stack.Count - count, count)])
                    {
                        ForwardedParameters = parameters.Any(static parameter => parameter >= 0) ? [.. parameters] : default,
                    });
                }
                switch (instruction)
                {
                    case { Operation: CilOperation.Nop }:
                        break;
                    case { Operation: CilOperation.LoadNull }:
                        Push(new ScalarConstant.Null());
                        break;
                    case { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 integer }:
                        Push(new ScalarConstant.Int32(integer.Value));
                        break;
                    case { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: var argument } }
                        when argument >= receiver && !changed.Contains(argument):
                        Push(null, argument - receiver);
                        break;
                    case
                    {
                        Operation: CilOperation.LoadArgument or CilOperation.LoadArgumentAddress or
                        CilOperation.LoadLocal or CilOperation.LoadLocalAddress or CilOperation.LoadString
                    }:
                        Push(null);
                        break;
                    case { Operation: CilOperation.Duplicate } when stack.Count != 0:
                        Push(stack[^1], forwarded[^1]);
                        break;
                    case { Operation: CilOperation.Pop } when stack.Count != 0:
                        stack.RemoveAt(stack.Count - 1);
                        forwarded.RemoveAt(forwarded.Count - 1);
                        break;
                    default:
                        // The next validated entry fixes the height: see above.
                        unmodeled = true;
                        break;
                }
            }

            void Push(ScalarConstant? constant, int parameter = -1)
            {
                stack.Add(constant);
                forwarded.Add(parameter);
            }
        }
        return result.ToImmutable();
    }
}
