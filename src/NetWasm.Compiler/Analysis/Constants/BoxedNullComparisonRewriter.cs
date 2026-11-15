// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Analysis.Constants;

internal sealed class BoxedNullComparisonRewriter(
    IControlFlowGraphBuilder graphs,
    INullableTypeResolver nullableTypes) : IMethodRewriteRule
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly INullableTypeResolver _nullableTypes = nullableTypes ?? throw new ArgumentNullException(nameof(nullableTypes));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var graph = _graphs.Build(body);
        var rewritten = body.Instructions.ToDictionary(instruction => instruction.Offset);
        var changed = false;
        foreach (var block in graph.Blocks)
        {
            var operations = block.Instructions.Where(instruction => instruction.Operation != CilOperation.Nop).ToArray();
            for (var index = 0; index + 1 < operations.Length; index++)
            {
                var box = operations[index];
                if (box is not { Operation: CilOperation.Box, Operand: CilOperand.TypeIdentity { Value: var type } } ||
                    !type.IsValueType || type.ContainsGenericParameters || _nullableTypes.Resolve(type) is not null)
                    continue;

                var test = operations[index + 1];
                if (test.Operation is CilOperation.BranchIfTrue or CilOperation.BranchIfFalse)
                {
                    rewritten[test.Offset] = test with
                    {
                        Operation = test.Operation == CilOperation.BranchIfTrue ? CilOperation.Branch : CilOperation.Nop,
                        Operand = test.Operation == CilOperation.BranchIfTrue ? test.Operand : new CilOperand.None(),
                    };
                }
                else if (test.Operation == CilOperation.LoadNull && index + 2 < operations.Length &&
                    operations[index + 2].Operation is CilOperation.CompareEqual or CilOperation.CompareGreaterThanUnsigned)
                {
                    var comparison = operations[index + 2];
                    rewritten[test.Offset] = test with
                    {
                        Operation = CilOperation.LoadInt32,
                        Operand = new CilOperand.ConstantI4(comparison.Operation == CilOperation.CompareEqual ? 0 : 1),
                    };
                    rewritten[comparison.Offset] = comparison with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
                }
                else continue;

                // Evaluate the original value (including side effects), but do
                // not materialize an object whose identity cannot escape.
                rewritten[box.Offset] = box with { Operation = CilOperation.Pop, Operand = new CilOperand.None() };
                changed = true;
            }
        }

        return changed ? body with { Instructions = [.. body.Instructions.Select(instruction => rewritten[instruction.Offset])] } : body;
    }
}
