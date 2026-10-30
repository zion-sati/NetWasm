using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

using static ConstantRewriteTestSupport;

public sealed class BoxedTypeTestRewriterTests
{
    private static readonly CliTypeIdentity Integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
    private static readonly CliTypeIdentity Long = CliTypeIdentity.FromStackKind(CliValueKind.I8);
    private static readonly CliTypeIdentity Formattable = CliTypeIdentity.Named(Assembly, "System", "IFormattable", false);

    [Theory]
    [InlineData(CilOperation.BranchIfTrue, CilOperation.Branch)]
    [InlineData(CilOperation.BranchIfFalse, CilOperation.Nop)]
    public void ABoxAlwaysPassesATestForItsOwnType(CilOperation branch, CilOperation expected)
    {
        var body = Body(
            Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(1, CilOperation.Box, new CilOperand.TypeIdentity(Integer)),
            Instruction(2, CilOperation.IsInstance, new CilOperand.TypeIdentity(Integer)),
            Instruction(3, branch, new CilOperand.BranchTarget(5)),
            Instruction(4, CilOperation.Return),
            Instruction(5, CilOperation.Return));

        var result = Rule(body).Rewrite(body);

        // The value is still evaluated, then dropped.
        Assert.Equal(
            [CilOperation.LoadArgument, CilOperation.Pop, CilOperation.Nop, expected],
            result.Instructions.Take(4).Select(static instruction => instruction.Operation));
        Assert.Equal(expected == CilOperation.Branch ? body.Instructions[3].Operand : new CilOperand.None(), result.Instructions[3].Operand);
        Assert.Equal(body.Instructions.Select(static instruction => instruction.Offset), result.Instructions.Select(static instruction => instruction.Offset));
    }

    [Fact]
    public void AnIsExpressionOnABoxIsTrue()
    {
        var body = Body(
            Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(1, CilOperation.Box, new CilOperand.TypeIdentity(Integer)),
            Instruction(2, CilOperation.IsInstance, new CilOperand.TypeIdentity(Integer)),
            Instruction(3, CilOperation.LoadNull),
            Instruction(4, CilOperation.CompareGreaterThanUnsigned),
            Instruction(5, CilOperation.Return));

        var result = Rule(body).Rewrite(body);

        Assert.Equal(
            [CilOperation.LoadArgument, CilOperation.Pop, CilOperation.Nop, CilOperation.LoadInt32, CilOperation.Nop, CilOperation.Return],
            result.Instructions.Select(static instruction => instruction.Operation));
        Assert.Equal(new CilOperand.ConstantI4(1), result.Instructions[3].Operand);
    }

    [Theory]
    [InlineData(CilOperation.BranchIfTrue, true, CilOperation.Branch)]
    [InlineData(CilOperation.BranchIfTrue, false, CilOperation.Nop)]
    [InlineData(CilOperation.BranchIfFalse, true, CilOperation.Nop)]
    [InlineData(CilOperation.BranchIfFalse, false, CilOperation.Branch)]
    public void ATestForAnotherTypeHasTheRuntimeTestsAnswer(CilOperation branch, bool assignable, CilOperation expected)
    {
        var body = Body(
            Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(1, CilOperation.Box, new CilOperand.TypeIdentity(Integer)),
            Instruction(2, CilOperation.IsInstance, new CilOperand.TypeIdentity(Formattable)),
            Instruction(3, branch, new CilOperand.BranchTarget(5)),
            Instruction(4, CilOperation.Return),
            Instruction(5, CilOperation.Return));
        var relationships = new TestTypeRelationshipClassifier(assignable);

        var result = Rule(body, relationships).Rewrite(body);

        Assert.Equal(
            [CilOperation.LoadArgument, CilOperation.Pop, CilOperation.Nop, expected],
            result.Instructions.Take(4).Select(static instruction => instruction.Operation));
        Assert.Equal([(Integer, Formattable)], relationships.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnIsExpressionForAnotherTypeIsTheRuntimeTestsAnswer(bool assignable)
    {
        var nullable = CliTypeIdentity.GenericInstantiation(CliTypeIdentity.Named(Assembly, "System", "Nullable`1", true), [Long]);
        var body = Body(
            Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(1, CilOperation.Box, new CilOperand.TypeIdentity(Integer)),
            Instruction(2, CilOperation.IsInstance, new CilOperand.TypeIdentity(nullable)),
            Instruction(3, CilOperation.LoadNull),
            Instruction(4, CilOperation.CompareGreaterThanUnsigned),
            Instruction(5, CilOperation.Return));
        var relationships = new TestTypeRelationshipClassifier(assignable);

        var result = Rule(body, relationships).Rewrite(body);

        Assert.Equal(
            [CilOperation.LoadArgument, CilOperation.Pop, CilOperation.Nop, CilOperation.LoadInt32, CilOperation.Nop, CilOperation.Return],
            result.Instructions.Select(static instruction => instruction.Operation));
        Assert.Equal(new CilOperand.ConstantI4(assignable ? 1 : 0), result.Instructions[3].Operand);
        // A nullable target admits its underlying type.
        Assert.Equal([(Integer, Long)], relationships.Calls);
    }

    [Theory]
    // unbox.any straight after the box, or after a test for the same type.
    [InlineData(false)]
    [InlineData(true)]
    public void UnboxingABoxYieldsTheValue(bool tested)
    {
        CilInstruction[] instructions = tested
            ?
            [
                Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
                Instruction(1, CilOperation.Box, new CilOperand.TypeIdentity(Integer)),
                Instruction(2, CilOperation.IsInstance, new CilOperand.TypeIdentity(Integer)),
                Instruction(3, CilOperation.UnboxAny, new CilOperand.TypeIdentity(Integer)),
                Instruction(4, CilOperation.Return),
            ]
            :
            [
                Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
                Instruction(1, CilOperation.Box, new CilOperand.TypeIdentity(Integer)),
                Instruction(2, CilOperation.UnboxAny, new CilOperand.TypeIdentity(Integer)),
                Instruction(3, CilOperation.Return),
            ];
        var body = Body(instructions);

        var result = Rule(body).Rewrite(body);

        Assert.Equal(CilOperation.LoadArgument, result.Instructions[0].Operation);
        Assert.All(result.Instructions.Skip(1).SkipLast(1), static instruction => Assert.Equal(CilOperation.Nop, instruction.Operation));
    }

    [Theory]
    // A reference, an open or a nullable type, a test that is not the box's.
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void LeavesOtherShapesAlone(int shape)
    {
        var nullable = CliTypeIdentity.GenericInstantiation(CliTypeIdentity.Named(Assembly, "System", "Nullable`1", true), [Integer]);
        var boxed = shape switch
        {
            1 => Owner,
            2 => CliTypeIdentity.GenericParameter(true, 0),
            3 => nullable,
            _ => Integer,
        };
        CilOperand test = shape switch
        {
            4 => new CilOperand.None(),
            _ => new CilOperand.TypeIdentity(boxed),
        };
        var operation = shape switch
        {
            5 => CilOperation.CastClass,
            _ => CilOperation.IsInstance,
        };
        var consumer = shape switch
        {
            6 => Instruction(3, CilOperation.StoreLocal, new CilOperand.Index(0)),
            7 => Instruction(3, CilOperation.LoadNull),
            8 => Instruction(3, CilOperation.UnboxAny, new CilOperand.TypeIdentity(Long)),
            _ => Instruction(3, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(5)),
        };
        var body = shape == 9
            // The box is too close to the end of its block to be followed by a test and its use.
            ? Body(
                Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
                Instruction(1, CilOperation.Box, new CilOperand.TypeIdentity(Integer)),
                Instruction(2, CilOperation.Return))
            : Body(
                Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
                Instruction(1, CilOperation.Box, new CilOperand.TypeIdentity(boxed)),
                Instruction(2, operation, test),
                consumer,
                Instruction(4, CilOperation.Return),
                Instruction(5, CilOperation.Return));

        Assert.Same(body, Rule(body).Rewrite(body));
    }

    [Fact]
    public void RejectsMissingDependenciesAndBody()
    {
        var body = Body(Instruction(0, CilOperation.Return));
        Assert.Throws<ArgumentNullException>(() => new BoxedTypeTestRewriter(null!, new NullableTypeResolver(), new TestTypeRelationshipClassifier()));
        Assert.Throws<ArgumentNullException>(() => new BoxedTypeTestRewriter(Graph(body), null!, new TestTypeRelationshipClassifier()));
        Assert.Throws<ArgumentNullException>(() => new BoxedTypeTestRewriter(Graph(body), new NullableTypeResolver(), null!));
        Assert.Throws<ArgumentNullException>(() => Rule(body).Rewrite(null!));
    }

    private static BoxedTypeTestRewriter Rule(CilMethodBody body, ITypeRelationshipClassifier? relationships = null) =>
        new(Graph(body), new NullableTypeResolver(), relationships ?? new TestTypeRelationshipClassifier(isAssignable: false));
}
