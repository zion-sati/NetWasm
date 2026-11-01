using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.LoopInvariantFields;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.LoopInvariantFields;

public sealed class AggregateFieldHoistingRewriterTests
{
    private static readonly AssemblyIdentity Assembly = new("FieldHoistingRewriter.Tests");
    private static readonly CliTypeIdentity Aggregate = CliTypeIdentity.Named(
        Assembly,
        "Fixtures",
        "Aggregate",
        isValueType: true);
    private static readonly CliTypeIdentity Int32 =
        CliTypeIdentity.FromStackKind(CliValueKind.I4);

    [Fact]
    public void RewriteCachesFieldBeforePreheaderAndReplacesLoopReads()
    {
        var body = Body();
        var plan = Plan(body);
        var rewriter = Assert.IsAssignableFrom<IMethodRewriteRule>(
            new AggregateFieldHoistingRewriter(new FixedPlanner(plan)));

        var rewritten = rewriter.Rewrite(body);

        Assert.Equal(13, rewritten.Instructions.Length);
        Assert.Equal(
            new[] { CliValueKind.ValueType, CliValueKind.I4 },
            rewritten.Locals);
        Assert.Equal(
            new[] { Aggregate, Int32 },
            rewritten.LocalSignatureTypes);
        Assert.Equal(CilOperation.LoadLocalAddress, rewritten.Instructions[2].Operation);
        Assert.Equal(new CilOperand.Index(0), rewritten.Instructions[2].Operand);
        Assert.Equal(CilOperation.LoadField, rewritten.Instructions[3].Operation);
        Assert.Equal(CilOperation.StoreLocal, rewritten.Instructions[4].Operation);
        Assert.Equal(new CilOperand.Index(1), rewritten.Instructions[4].Operand);
        Assert.Equal(
            new CilOperand.BranchTarget(10),
            rewritten.Instructions[5].Operand);
        Assert.Equal(CilOperation.LoadLocal, rewritten.Instructions[6].Operation);
        Assert.Equal(new CilOperand.Index(1), rewritten.Instructions[6].Operand);
        Assert.Equal(CilOperation.Nop, rewritten.Instructions[7].Operation);
        Assert.Equal(new CilOperand.None(), rewritten.Instructions[7].Operand);
        Assert.Equal(
            new CilOperand.BranchTarget(6),
            rewritten.Instructions[11].Operand);
        Assert.Equal(3, rewritten.Instructions[2].SourceOffset);
        Assert.Equal(4, rewritten.Instructions[3].SourceOffset);
        Assert.Equal(4, rewritten.Instructions[4].SourceOffset);
        Assert.Equal(3, rewritten.Instructions[6].SourceOffset);
        Assert.Equal(4, rewritten.Instructions[7].SourceOffset);
    }

    [Fact]
    public void RewriteReturnsUnchangedBodyForEmptyPlan()
    {
        var body = Body();
        var rewritten = new AggregateFieldHoistingRewriter(
            new FixedPlanner(AggregateFieldHoistingPlan.Empty)).Rewrite(body);

        Assert.Same(body, rewritten);
    }

    [Fact]
    public void RewriteRemapsBranchTargetsAndExceptionRegions()
    {
        var body = Body();
        body = body with
        {
            Instructions =
            [
                .. body.Instructions.Select(instruction => instruction.Offset == 5
                    ? instruction with
                    {
                        Operation = CilOperation.BranchIfTrue,
                        Operand = new CilOperand.BranchTarget(7),
                    }
                    : instruction),
            ],
            ExceptionRegions =
            [
                new CilExceptionRegion(
                    CilExceptionRegionKind.Finally,
                    0,
                    2,
                    2,
                    1,
                    null,
                    null),
                new CilExceptionRegion(
                    CilExceptionRegionKind.Filter,
                    3,
                    2,
                    5,
                    2,
                    null,
                    3),
            ],
        };

        var rewritten = new AggregateFieldHoistingRewriter(
            new FixedPlanner(Plan(body))).Rewrite(body);

        Assert.Equal(10, Assert.IsType<CilOperand.BranchTarget>(rewritten.Instructions[8].Operand).Offset);
        Assert.Collection(
            rewritten.ExceptionRegions,
            region =>
            {
                Assert.Equal(0, region.TryOffset);
                Assert.Equal(2, region.TryLength);
                Assert.Equal(2, region.HandlerOffset);
                Assert.Equal(4, region.HandlerLength);
                Assert.Null(region.FilterOffset);
            },
            region =>
            {
                Assert.Equal(6, region.TryOffset);
                Assert.Equal(2, region.TryLength);
                Assert.Equal(8, region.HandlerOffset);
                Assert.Equal(2, region.HandlerLength);
                Assert.Equal(6, region.FilterOffset);
            });
    }

    [Fact]
    public void RewriteRejectsMissingInputsAndInvalidPlans()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AggregateFieldHoistingRewriter(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new AggregateFieldHoistingRewriter(
                new FixedPlanner(AggregateFieldHoistingPlan.Empty)).Rewrite(null!));
        Assert.Throws<InvalidOperationException>(() =>
            new AggregateFieldHoistingRewriter(new NullPlanner()).Rewrite(Body()));

        var body = Body();
        var hoist = Plan(body).Hoists[0];
        Assert.Throws<InvalidOperationException>(() => Rewrite(
            body,
            new AggregateFieldHoistingPlan(
                [hoist with { CachedLocalIndex = 2 }])));
        Assert.Throws<InvalidOperationException>(() => Rewrite(
            body,
            new AggregateFieldHoistingPlan(
                [hoist with { AddressOffsets = [] }])));
        Assert.Throws<InvalidOperationException>(() => Rewrite(
            body,
            new AggregateFieldHoistingPlan(
                [hoist with { PreheaderTerminatorOffset = 99 }])));
        Assert.Throws<InvalidOperationException>(() => Rewrite(
            body,
            new AggregateFieldHoistingPlan(
                [
                    hoist,
                    hoist with
                    {
                        CachedLocalIndex = 2,
                        FieldType = Int32,
                    },
                ])));
    }

    private static void Rewrite(
        CilMethodBody body,
        AggregateFieldHoistingPlan plan) =>
        new AggregateFieldHoistingRewriter(new FixedPlanner(plan)).Rewrite(body);

    private static AggregateFieldHoistingPlan Plan(CilMethodBody body)
    {
        return new(
        [
            new AggregateFieldHoist(
                2,
                0,
                1,
                Int32,
                body.Instructions[3],
                body.Instructions[4],
                [3],
                [4]),
        ]);
    }

    private static CilMethodBody Body()
    {
        var field = new FieldInstanceModel(
            new FieldDefinitionModel(
                new(Assembly, 0x04000001),
                new(Assembly, 0x02000001),
                "Count",
                Int32,
                isStatic: false),
            Aggregate,
            Int32);
        return new CilMethodBody(
            new MethodDefinitionModel(
                new(Assembly, 0x06000001),
                new(Assembly, 0x02000002),
                "Run",
                IsStatic: true,
                MethodSignatureModel.Create(CliValueKind.Void),
                RelativeVirtualAddress: 1),
            1,
            [CliValueKind.ValueType],
            [
                new CilInstruction(
                    0,
                    1,
                    CilOperation.DefaultValue,
                    new CilOperand.TypeIdentity(Aggregate)),
                new CilInstruction(1, 2, CilOperation.StoreLocal, new CilOperand.Index(0)),
                new CilInstruction(2, 3, CilOperation.Branch, new CilOperand.BranchTarget(7)),
                new CilInstruction(3, 4, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
                new CilInstruction(4, 5, CilOperation.LoadField, new CilOperand.FieldInstance(field)),
                new CilInstruction(5, 6, CilOperation.Pop, new CilOperand.None()),
                new CilInstruction(6, 7, CilOperation.Nop, new CilOperand.None()),
                new CilInstruction(7, 8, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                new CilInstruction(8, 9, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(3)),
                new CilInstruction(9, 10, CilOperation.Return, new CilOperand.None()),
            ])
        {
            LocalSignatureTypes = [Aggregate],
        };
    }

    private sealed class FixedPlanner(AggregateFieldHoistingPlan plan) :
        IAggregateFieldHoistingPlanner
    {
        public AggregateFieldHoistingPlan Plan(CilMethodBody body) => plan;
    }

    private sealed class NullPlanner : IAggregateFieldHoistingPlanner
    {
        public AggregateFieldHoistingPlan Plan(CilMethodBody body) => null!;
    }
}
