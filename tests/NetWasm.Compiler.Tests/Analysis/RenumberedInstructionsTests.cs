// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis;

// What a splice makes from an instruction keeps where it came from and reports the line it reported: a return folded
// into the branch to a shared epilogue reports the epilogue's sequence point through later rewrites.
public sealed class RenumberedInstructionsTests
{
    [Fact]
    public void ASplicedInstructionKeepsItsSourceAndItsLine()
    {
        var method = new MethodDefinitionModel(new(new("splice"), 0x06000001), new(new("splice"), 0x02000001), "M", true,
            MethodSignatureModel.Create(CliValueKind.Void), 1);
        var body = new CilMethodBody(method, 1, [],
        [
            new CilInstruction(0, 1, CilOperation.Nop, new CilOperand.None()),
            new CilInstruction(1, 2, CilOperation.Return, new CilOperand.None())
            {
                OriginalOffset = 10,
                SequencePointOffset = 20,
            },
        ]);

        var spliced = RenumberedInstructions.Splice(body,
        [
            [body.Instructions[0]],
            [new CilInstruction(0, 1, CilOperation.Nop, new CilOperand.None()), body.Instructions[1]],
        ]);

        Assert.Equal([0, 10, 10], spliced.Instructions.Select(instruction => instruction.SourceOffset));
        Assert.Equal([0, 20, 20], spliced.Instructions.Select(instruction => instruction.LineOffset));
    }
}
