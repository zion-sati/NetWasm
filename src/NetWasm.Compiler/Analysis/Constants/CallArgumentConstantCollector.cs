using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
        foreach (var block in method.ControlFlow.Graph.Blocks)
        {
            var stack = new List<ScalarConstant?>();
            var unmodelled = false;
            foreach (var instruction in block.Instructions)
            {
                if (!method.ControlFlow.InstructionEntryStacks.TryGetValue(instruction.Offset, out var entry)) continue;
                if (unmodelled)
                {
                    // An unmodelled instruction pops from the top and pushes at most one value (dup is modelled), so
                    // the entries below what it touched keep their facts and the top is unknown.
                    var kept = entry.Length == 0 ? 0 : Math.Min(stack.Count, entry.Length - 1);
                    stack.RemoveRange(kept, stack.Count - kept);
                    while (stack.Count < entry.Length) stack.Add(null);
                    unmodelled = false;
                }
                else if (stack.Count != entry.Length)
                {
                    stack.Clear();
                    for (var index = 0; index < entry.Length; index++) stack.Add(null);
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
                    result.Add(new(target, [.. stack.GetRange(stack.Count - count, count)]));
                }
                switch (instruction)
                {
                    case { Operation: CilOperation.Nop }:
                        break;
                    case { Operation: CilOperation.LoadNull }:
                        stack.Add(new ScalarConstant.Null());
                        break;
                    case { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 integer }:
                        stack.Add(new ScalarConstant.Int32(integer.Value));
                        break;
                    case
                    {
                        Operation: CilOperation.LoadArgument or CilOperation.LoadArgumentAddress or
                        CilOperation.LoadLocal or CilOperation.LoadLocalAddress or CilOperation.LoadString
                    }:
                        stack.Add(null);
                        break;
                    case { Operation: CilOperation.Duplicate } when stack.Count != 0:
                        stack.Add(stack[^1]);
                        break;
                    case { Operation: CilOperation.Pop } when stack.Count != 0:
                        stack.RemoveAt(stack.Count - 1);
                        break;
                    default:
                        // The next validated entry fixes the height: see above.
                        unmodelled = true;
                        break;
                }
            }
        }
        return result.ToImmutable();
    }
}
