using System;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Analysis.Constants;

// `value is int number` on a type parameter compiles to box, isinst and
// unbox.any, and `value is IFormattable` to box, isinst and a branch. Once the
// parameter is a value type, the boxed object is never visible to anything
// else: a test of it gives the answer the runtime test gives for its type, and
// unboxing it yields the value again, so neither needs an allocation.
internal sealed class BoxedTypeTestRewriter(
    IControlFlowGraphBuilder graphs,
    INullableTypeResolver nullableTypes,
    ITypeRelationshipClassifier relationships) : IMethodRewriteRule
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly INullableTypeResolver _nullableTypes = nullableTypes ?? throw new ArgumentNullException(nameof(nullableTypes));
    private readonly ITypeRelationshipClassifier _relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var graph = _graphs.Build(body);
        var rewritten = body.Instructions.ToDictionary(instruction => instruction.Offset);
        var changed = false;
        foreach (var block in graph.Blocks)
        {
            var operations = block.Instructions.Where(instruction => instruction.Operation != CilOperation.Nop).ToArray();
            for (var index = 0; index + 2 < operations.Length; index++)
            {
                // A nullable value boxes to null or to its underlying type.
                if (operations[index] is not { Operation: CilOperation.Box, Operand: CilOperand.TypeIdentity { Value: var type } } box ||
                    !type.IsValueType || type.ContainsGenericParameters || _nullableTypes.Resolve(type) is not null ||
                    operations[index + 1] is not { Operand: CilOperand.TypeIdentity { Value: var tested } } test ||
                    tested.ContainsGenericParameters)
                {
                    continue;
                }
                var consumer = operations[index + 2];
                if (test.Operation == CilOperation.UnboxAny && tested.Equals(type))
                {
                    rewritten[box.Offset] = Nop(box);
                    rewritten[test.Offset] = Nop(test);
                }
                else if (test.Operation == CilOperation.IsInstance && tested.Equals(type) &&
                    consumer is { Operation: CilOperation.UnboxAny, Operand: CilOperand.TypeIdentity { Value: var unboxed } } && unboxed.Equals(type))
                {
                    rewritten[box.Offset] = Nop(box);
                    rewritten[test.Offset] = Nop(test);
                    rewritten[consumer.Offset] = Nop(consumer);
                }
                else if (test.Operation == CilOperation.IsInstance &&
                    consumer.Operation is CilOperation.BranchIfTrue or CilOperation.BranchIfFalse)
                {
                    rewritten[box.Offset] = box with { Operation = CilOperation.Pop, Operand = new CilOperand.None() };
                    rewritten[test.Offset] = Nop(test);
                    rewritten[consumer.Offset] = consumer.Operation == CilOperation.BranchIfTrue == Passes(type, tested)
                        ? consumer with { Operation = CilOperation.Branch }
                        : Nop(consumer);
                }
                else if (test.Operation == CilOperation.IsInstance && consumer.Operation == CilOperation.LoadNull &&
                    index + 3 < operations.Length && operations[index + 3].Operation == CilOperation.CompareGreaterThanUnsigned)
                {
                    // `is` as a value: whether the object passes.
                    rewritten[box.Offset] = box with { Operation = CilOperation.Pop, Operand = new CilOperand.None() };
                    rewritten[test.Offset] = Nop(test);
                    rewritten[consumer.Offset] = consumer with
                    {
                        Operation = CilOperation.LoadInt32,
                        Operand = new CilOperand.ConstantI4(Passes(type, tested) ? 1 : 0),
                    };
                    rewritten[operations[index + 3].Offset] = Nop(operations[index + 3]);
                }
                else
                {
                    continue;
                }
                changed = true;
            }
        }

        return changed ? body with { Instructions = [.. body.Instructions.Select(instruction => rewritten[instruction.Offset])] } : body;
    }

    // The runtime test admits the types the classifier relates to the target,
    // with a nullable target standing for its underlying type.
    private bool Passes(CliTypeIdentity type, CliTypeIdentity tested) =>
        tested.Equals(type) ||
        _relationships.Classify(type, _nullableTypes.Resolve(tested) ?? tested).IsAssignmentCompatible;

    private static CilInstruction Nop(CilInstruction instruction) =>
        instruction with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
}
