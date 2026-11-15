// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class RectangularArrayCallRewriterTests
{
    private static readonly AssemblyIdentity Assembly = new("Rectangular.Tests");
    private static readonly CliTypeIdentity Element =
        CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Array = CliTypeIdentity.Array(Element, 2);
    private static readonly MethodDefinitionModel HostMethod = new(
        new EntityKey(Assembly, 0x06000001),
        new EntityKey(Assembly, 0x02000001),
        "Run",
        true,
        MethodSignatureModel.Create(CliValueKind.Void),
        1);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RewritePreservesExplicitConstructorBounds(int rank)
    {
        var array = CliTypeIdentity.Array(Element, rank);
        var method = Method(".ctor", array) with
        {
            Signature = MethodSignatureModel.Create(CliValueKind.Void,
                [.. Enumerable.Repeat(CliValueKind.I4, rank * 2)]),
        };
        var rewriter = new RectangularArrayCallRewriter();

        var result = ((IMethodRewriteRule)rewriter).Rewrite(
            Body(Instruction(CilOperation.NewObject, method)));

        Assert.Equal(CilOperation.NewBoundedRectangularArray,
            Assert.Single(result.Instructions).Operation);
    }

    [Theory]
    [InlineData(CilOperation.NewObject, ".ctor", CilOperation.NewRectangularArray)]
    [InlineData(CilOperation.Call, "Get", CilOperation.LoadRectangularArrayElement)]
    [InlineData(CilOperation.Call, "Set", CilOperation.StoreRectangularArrayElement)]
    [InlineData(CilOperation.Call, "Address", CilOperation.LoadRectangularArrayElementAddress)]
    public void RewriteMapsEveryRuntimeProvidedMemberThroughItsInterface(
        CilOperation source,
        string name,
        CilOperation expected)
    {
        var rewriter = new RectangularArrayCallRewriter();
        var body = Body(Instruction(source, Method(name, Array)));

        var rewritten = ((IMethodRewriteRule)rewriter).Rewrite(body);

        var instruction = Assert.Single(rewritten.Instructions);
        Assert.Equal(expected, instruction.Operation);
        Assert.Same(Array, Assert.IsType<CilOperand.TypeIdentity>(instruction.Operand).Value);
    }

    [Theory]
    [InlineData(CilOperation.Call, ".ctor")]
    [InlineData(CilOperation.NewObject, "Get")]
    [InlineData(CilOperation.CallVirtual, "Set")]
    [InlineData(CilOperation.NewObject, "Address")]
    public void RewriteRejectsInvalidOperationAndMemberPairs(
        CilOperation operation,
        string name)
    {
        var rewriter = new RectangularArrayCallRewriter();

        var exception = Assert.Throws<CompilerException>(() =>
            ((IMethodRewriteRule)rewriter).Rewrite(
                Body(Instruction(operation, Method(name, Array)))));

        Assert.Equal(DiagnosticCode.UnsupportedCil, exception.Diagnostic.Code);
        Assert.Equal(11, exception.Diagnostic.IlOffset);
    }

    [Fact]
    public void RewriteLeavesOrdinaryInstructionsAndMethodsUnchanged()
    {
        var rewriter = new RectangularArrayCallRewriter();
        var ordinaryType = CliTypeIdentity.Named(Assembly, "Tests", "Value", false);
        var noOperand = new CilInstruction(1, 2, CilOperation.Nop, new CilOperand.None());
        var ordinaryCall = Instruction(CilOperation.Call, Method("Get", ordinaryType));
        var body = Body(noOperand, ordinaryCall);

        var rewritten = ((IMethodRewriteRule)rewriter).Rewrite(body);

        Assert.Equal(2, rewritten.Instructions.Length);
        Assert.Same(noOperand, rewritten.Instructions[0]);
        Assert.Same(ordinaryCall, rewritten.Instructions[1]);
    }

    [Fact]
    public void RewriteRejectsNullBodyThroughItsInterface()
    {
        var rewriter = new RectangularArrayCallRewriter();

        Assert.Throws<ArgumentNullException>(() =>
            ((IMethodRewriteRule)rewriter).Rewrite(null!));
    }

    private static CilInstruction Instruction(
        CilOperation operation,
        MethodInstanceModel method) => new(
        11,
        16,
        operation,
        new CilOperand.MethodInstance(method));

    private static MethodInstanceModel Method(string name, CliTypeIdentity declaringType)
    {
        var definition = new MethodDefinitionModel(
            new EntityKey(Assembly, 0x0a000001),
            new EntityKey(Assembly, 0x1b000001),
            name,
            false,
            MethodSignatureModel.Create(CliValueKind.Void,
                [.. Enumerable.Repeat(CliValueKind.I4, declaringType.ArrayRank)]),
            0);
        return new(definition, declaringType, [], definition.Signature);
    }

    private static CilMethodBody Body(params CilInstruction[] instructions) => new(
        HostMethod,
        4,
        [],
        [.. instructions]);
}
