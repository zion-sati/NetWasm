// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

internal sealed class ClosedTypeComparisonRewriter(
    IControlFlowGraphBuilder graphs,
    ICalledMethodResolver calledMethods,
    IPureReferenceComparisonClassifier comparisons,
    IClosedTypeIdentityComparer types) : IMethodRewriteRule
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));
    private readonly IPureReferenceComparisonClassifier _comparisons = comparisons ?? throw new ArgumentNullException(nameof(comparisons));
    private readonly IClosedTypeIdentityComparer _types = types ?? throw new ArgumentNullException(nameof(types));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var graph = _graphs.Build(body);
        var rewritten = body.Instructions.ToDictionary(instruction => instruction.Offset);
        var changed = false;
        foreach (var block in graph.Blocks)
        {
            var operations = block.Instructions.Where(instruction => instruction.Operation != CilOperation.Nop).ToArray();
            for (var index = 4; index < operations.Length; index++)
            {
                if (operations[index - 4] is not { Operation: CilOperation.LoadTypeToken, Operand: CilOperand.TypeIdentity left } ||
                    operations[index - 3].Operation != CilOperation.MaterializeType ||
                    operations[index - 2] is not { Operation: CilOperation.LoadTypeToken, Operand: CilOperand.TypeIdentity right } ||
                    operations[index - 1].Operation != CilOperation.MaterializeType ||
                    _types.Compare(left.Value, right.Value) is not { } equal)
                {
                    continue;
                }

                var comparison = operations[index];
                if (comparison.Operation is CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual)
                {
                    for (var input = index - 4; input < index - 1; input++)
                    {
                        var instruction = operations[input];
                        rewritten[instruction.Offset] = instruction with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
                    }
                    var constant = operations[index - 1];
                    rewritten[constant.Offset] = constant with { Operation = CilOperation.LoadInt32, Operand = new CilOperand.ConstantI4(equal ? 1 : 0) };
                    rewritten[comparison.Offset] = comparison with
                    {
                        Operation = comparison.Operation == CilOperation.BranchIfEqual ? CilOperation.BranchIfTrue : CilOperation.BranchIfFalse,
                    };
                    changed = true;
                    continue;
                }

                var kind = comparison.Operation == CilOperation.CompareEqual
                    ? ReferenceComparison.Equal
                    : comparison.Operation == CilOperation.Call && _calledMethods.Resolve(comparison) is { } method
                        ? _comparisons.Classify(method)
                        : null;
                if (kind is null) continue;

                for (var input = index - 4; input < index; input++)
                {
                    var instruction = operations[input];
                    rewritten[instruction.Offset] = instruction with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
                }
                rewritten[comparison.Offset] = comparison with
                {
                    Operation = CilOperation.LoadInt32,
                    Operand = new CilOperand.ConstantI4(equal == (kind == ReferenceComparison.Equal) ? 1 : 0),
                };
                changed = true;
            }
        }

        return changed ? body with { Instructions = [.. body.Instructions.Select(instruction => rewritten[instruction.Offset])] } : body;
    }
}
