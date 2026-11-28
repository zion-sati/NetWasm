// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

// A tiny assembler: instructions numbered in order, branch operands named by labels.
internal sealed class Il
{
    private readonly List<(CilOperation Operation, object? Operand)> _instructions = [];
    private readonly Dictionary<string, int> _labels = [];

    public void Label(string name) => _labels[name] = _instructions.Count;

    public void Emit(CilOperation operation, object? operand = null) => _instructions.Add((operation, operand));

    public int Offset(string label) => _labels[label];

    // The method's int locals.
    public int Locals { get; init; } = 1;

    public int MaxStack { get; init; } = 2;

    public CilMethodBody Body(params CilExceptionRegion[] regions) => ControlFlowTestSupport.Body(CliValueKind.I4, MaxStack,
        [.. Enumerable.Repeat(CliValueKind.I4, Locals)],
        [.. _instructions.Select((instruction, offset) => I(offset, instruction.Operation, instruction.Operand switch
        {
            string label => new CilOperand.BranchTarget(_labels[label]),
            int constant => new CilOperand.ConstantI4(constant),
            CilOperand operand => operand,
            _ => null,
        }))]) with
    { ExceptionRegions = [.. regions] };
}
