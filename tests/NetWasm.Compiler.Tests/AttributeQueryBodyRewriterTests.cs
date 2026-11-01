using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Attributes;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class AttributeQueryBodyRewriterTests
{
    [Fact]
    public void NoQueriesPreservesTheOriginalBody()
    {
        var body = Body(I(0, CilOperation.Return));

        Assert.Same(body, Rewrite(new AttributeQueryBodyRewriter(), body, []));
    }

    [Fact]
    public void ReplacementPreservesSourceLocationsAndMovesIncomingAndOutgoingBranches()
    {
        var body = Body(
            I(0, CilOperation.Branch, new CilOperand.BranchTarget(10)),
            I(10, CilOperation.Call) with { OriginalOffset = 71 },
            I(20, CilOperation.Branch, new CilOperand.BranchTarget(0)),
            I(30, CilOperation.Return));
        var fragment = new AttributeCilFragment(
            [I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
             I(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(3)),
             I(2, CilOperation.Nop),
             I(3, CilOperation.LoadNull)], [], 2);

        var actual = Rewrite(new AttributeQueryBodyRewriter(), body,
            ImmutableDictionary<int, AttributeCilFragment>.Empty.Add(10, fragment));

        Assert.Equal(7, actual.Instructions.Length);
        Assert.Equal(new CilOperand.BranchTarget(1), actual.Instructions[0].Operand);
        Assert.Equal(new CilOperand.BranchTarget(4), actual.Instructions[2].Operand);
        Assert.Equal(new CilOperand.BranchTarget(0), actual.Instructions[5].Operand);
        Assert.Equal([0, 71, 71, 71, 71, 20, 30], actual.Instructions.Select(i => i.SourceOffset));
        Assert.Equal(Enumerable.Range(0, 7), actual.Instructions.Select(i => i.Offset));
        Assert.Equal(Enumerable.Range(1, 7), actual.Instructions.Select(i => i.NextOffset));
        Assert.Same(body.Method, actual.Method);
        Assert.Same(body.MethodInstance, actual.MethodInstance);
    }

    [Fact]
    public void BranchTargetsUseTheirOwningCoordinateSpace()
    {
        var body = Body(
            I(0, CilOperation.Branch, new CilOperand.BranchTarget(20)),
            I(10, CilOperation.Call),
            I(20, CilOperation.Return));
        var fragment = new AttributeCilFragment(
            [I(0, CilOperation.Branch, new CilOperand.BranchTarget(2)),
             I(1, CilOperation.Nop), I(2, CilOperation.Nop)], [], 1);

        var actual = Rewrite(new AttributeQueryBodyRewriter(), body,
            ImmutableDictionary<int, AttributeCilFragment>.Empty.Add(10, fragment));

        Assert.Equal(4, Assert.IsType<CilOperand.BranchTarget>(actual.Instructions[0].Operand).Offset);
        Assert.Equal(3, Assert.IsType<CilOperand.BranchTarget>(actual.Instructions[1].Operand).Offset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExceptionBoundariesEncloseExpandedQueriesIncludingTheMethodEnd(bool filter)
    {
        var body = Body(I(0, CilOperation.Call), I(10, CilOperation.Nop),
            I(20, CilOperation.Call), I(30, CilOperation.Return)) with
        {
            ExceptionRegions = [new(filter ? CilExceptionRegionKind.Filter : CilExceptionRegionKind.Catch,
                0, 10, 20, 11, null, filter ? 10 : null)],
        };
        var fragment = new AttributeCilFragment([I(0, CilOperation.Nop), I(1, CilOperation.Nop)], [], 0);

        var actual = Rewrite(new AttributeQueryBodyRewriter(), body,
            ImmutableDictionary<int, AttributeCilFragment>.Empty.Add(0, fragment).Add(20, fragment));

        var region = Assert.Single(actual.ExceptionRegions);
        Assert.Equal(0, region.TryOffset);
        Assert.Equal(2, region.TryLength);
        Assert.Equal(3, region.HandlerOffset);
        Assert.Equal(3, region.HandlerLength);
        Assert.Equal(filter ? 2 : (int?)null, region.FilterOffset);
        Assert.Equal(body.ExceptionRegions[0].Kind, region.Kind);
    }

    [Fact]
    public void AppendsTypedLocalsInInstructionOrderAndReservesSurvivingStackValues()
    {
        var first = CliTypeIdentity.Named(new("Tests"), "Example", "First", false);
        var second = CliTypeIdentity.SzArray(first);
        var body = Body(I(10, CilOperation.Call), I(20, CilOperation.Call), I(30, CilOperation.Return));
        var replacements = ImmutableDictionary<int, AttributeCilFragment>.Empty
            .Add(20, new([I(0, CilOperation.LoadLocal, new CilOperand.Index(2))], [second], 3))
            .Add(10, new([I(0, CilOperation.LoadLocal, new CilOperand.Index(1))], [first], 5));

        var actual = Rewrite(new AttributeQueryBodyRewriter(), body, replacements);

        Assert.Equal<CliTypeIdentity>([body.LocalSignatureTypes[0], first, second], actual.LocalSignatureTypes);
        Assert.Equal<CliValueKind>([CliValueKind.I4, CliValueKind.ManagedReference, CliValueKind.ManagedReference],
            actual.Locals);
        Assert.Equal(new CilOperand.Index(1), actual.Instructions[0].Operand);
        Assert.Equal(new CilOperand.Index(2), actual.Instructions[1].Operand);
        Assert.Equal(body.MaxStack + 5, actual.MaxStack);
    }

    [Fact]
    public void RejectsReplacementOutsideTheMethod()
    {
        var fragment = new AttributeCilFragment([I(0, CilOperation.Nop)], [], 0);
        Assert.Throws<ArgumentException>(() => Rewrite(new AttributeQueryBodyRewriter(),
            Body(I(0, CilOperation.Return)),
            ImmutableDictionary<int, AttributeCilFragment>.Empty.Add(7, fragment)));
    }

    [Fact]
    public void RejectsEmptyReplacement()
    {
        Assert.Throws<ArgumentException>(() => Rewrite(new AttributeQueryBodyRewriter(),
            Body(I(0, CilOperation.Call)),
            ImmutableDictionary<int, AttributeCilFragment>.Empty.Add(0, new([], [], 0))));
    }

    [Fact]
    public void RejectsMissingInputs()
    {
        Assert.Throws<ArgumentNullException>(() => Rewrite(new AttributeQueryBodyRewriter(), null!, []));
        Assert.Throws<ArgumentNullException>(() => Rewrite(new AttributeQueryBodyRewriter(),
            Body(I(0, CilOperation.Return)), null!));
    }

    private static CilMethodBody Rewrite<TRewriter>(TRewriter rewriter, CilMethodBody body,
        ImmutableDictionary<int, AttributeCilFragment> replacements)
        where TRewriter : IAttributeQueryBodyRewriter => rewriter.Rewrite(body, replacements);

    private static CilInstruction I(int offset, CilOperation operation, CilOperand? operand = null) =>
        new(offset, offset + 1, operation, operand ?? new CilOperand.None());

    private static CilMethodBody Body(params CilInstruction[] instructions)
    {
        var assembly = new AssemblyIdentity("Tests");
        var method = new MethodDefinitionModel(new(assembly, 1), new(assembly, 2), "Run", true,
            MethodSignatureModel.Create(CliValueKind.ManagedReference), 1);
        return new(method, 3, [CliValueKind.I4], [.. instructions])
        {
            MethodInstance = new(method, CliTypeIdentity.Named(assembly, "Example", "Program", false), [], method.Signature),
        };
    }
}
