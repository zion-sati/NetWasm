using System;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// Replaces a call whose value the project's feature switches select (a feature switch's getter, or a Boolean method an
// embedded ILLink.Substitutions.xml stubs) with that value, as ILLink substitutes it, so the constant-branch rules that
// follow remove the code it guards before reachability. A call that takes a receiver or arguments (EventSource
// .IsEnabled(), called on a source's static Log) folds where each of them is pushed by a load without effects, which
// goes too; as where ILLink folds such a call, the receiver is not tested for null. A call that dispatches to an
// override keeps the call.
internal sealed class FeatureSwitchCallRewriter(
    IControlFlowGraphBuilder graphs,
    ICalledMethodResolver calledMethods,
    IEffectFreeLoadFinder loads,
    IFeatureSwitchValueSelector values) : IMethodRewriteRule
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));
    private readonly IEffectFreeLoadFinder _loads = loads ?? throw new ArgumentNullException(nameof(loads));
    private readonly IFeatureSwitchValueSelector _values = values ?? throw new ArgumentNullException(nameof(values));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        // Most bodies call nothing a switch selects; only the others need their blocks.
        if (!body.Instructions.Any(instruction => Fold(instruction) is not null))
        {
            return body;
        }

        var rewritten = body.Instructions.ToDictionary(instruction => instruction.Offset);
        var changed = false;
        foreach (var block in _graphs.Build(body).Blocks)
        {
            var operations = block.Instructions.Where(instruction => instruction.Operation != CilOperation.Nop).ToArray();
            for (var index = 0; index < operations.Length; index++)
            {
                if (Fold(operations[index]) is not var (target, value))
                {
                    continue;
                }
                var start = index;
                var count = target.Signature.ParameterSignatureTypes.Length + (target.Definition.IsStatic ? 0 : 1);
                for (var argument = 0; argument < count && start >= 0; argument++)
                {
                    start = _loads.FindStart(operations, start);
                }
                if (start < 0)
                {
                    continue;
                }

                for (var load = start; load < index; load++)
                {
                    operations[load] = rewritten[operations[load].Offset] = operations[load] with
                    {
                        Operation = CilOperation.Nop,
                        Operand = new CilOperand.None(),
                    };
                }
                operations[index] = rewritten[operations[index].Offset] = operations[index] with
                {
                    Operation = CilOperation.LoadInt32,
                    Operand = new CilOperand.ConstantI4(value ? 1 : 0),
                };
                changed = true;
            }
        }
        return changed ? body with { Instructions = [.. body.Instructions.Select(instruction => rewritten[instruction.Offset])] } : body;
    }

    // The Boolean method a call reaches and the value the switches select for it, where the call reaches it alone.
    private (MethodInstanceModel Target, bool Value)? Fold(CilInstruction call) =>
        call.Operation is CilOperation.Call or CilOperation.CallVirtual &&
        _calledMethods.Resolve(call) is { } target &&
        target.Signature.ReturnSignatureType.CanonicalName == "primitive:bool" &&
        !(call.Operation == CilOperation.CallVirtual && target.Definition.IsVirtual && !target.Definition.IsFinal) &&
        _values.Select(target.Definition) is { } value
            ? (target, value)
            : null;
}
