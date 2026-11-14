using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

using static ConstantRewriteTestSupport;
using static ConstantOperandReaderTests;
using static ConstantComparisonEvaluatorTests;

public sealed class ConstantCallRewriterTests
{
    [Fact]
    public void RejectsMissingDependenciesBeforeEvaluating()
    {
        var body = Body(Instruction(0, CilOperation.Return));
        var graphs = Graph(body);
        var calls = new MethodFacts();
        var evaluator = new RecordingEvaluator(null);
        Assert.Throws<ArgumentNullException>(() => new ConstantCallRewriter(null!, calls, evaluator, Operands(new MethodFacts()), Loads(new MethodFacts())));
        Assert.Throws<ArgumentNullException>(() => new ConstantCallRewriter(graphs, null!, evaluator, Operands(new MethodFacts()), Loads(new MethodFacts())));
        Assert.Throws<ArgumentNullException>(() => new ConstantCallRewriter(graphs, calls, null!, Operands(new MethodFacts()), Loads(new MethodFacts())));
        Assert.Throws<ArgumentNullException>(() => new ConstantCallRewriter(graphs, calls, evaluator, null!, Loads(new MethodFacts())));
        Assert.Throws<ArgumentNullException>(() => new ConstantCallRewriter(graphs, calls, evaluator, Operands(new MethodFacts()), null!));
        Assert.Throws<ArgumentNullException>(() => AsRule(new ConstantCallRewriter(graphs, calls, evaluator, Operands(new MethodFacts()), Loads(new MethodFacts()))).Rewrite(null!));
        Assert.Empty(evaluator.Requests);
    }

    [Fact]
    public void ForwardsIntegerLiteralsAndSkipsInstanceCalls()
    {
        var method = Method();
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4, CliValueKind.I4);
        method = method with { Signature = signature, Definition = method.Definition with { Signature = signature } };
        var body = Body(
            Instruction(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(-7)),
            Instruction(1, CilOperation.Nop),
            Instruction(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(42)),
            Instruction(3, CilOperation.Call, new CilOperand.MethodInstance(method)),
            Instruction(4, CilOperation.Return));
        var evaluator = new RecordingEvaluator(new ScalarConstant.Int32(35));
        var rule = AsRule(new ConstantCallRewriter(Graph(body), new MethodFacts(), evaluator, Operands(new MethodFacts()), Loads(new MethodFacts())));

        var result = rule.Rewrite(body);

        Assert.Equal(new ScalarConstant?[] { new ScalarConstant.Int32(-7), new ScalarConstant.Int32(42) },
            Assert.Single(evaluator.Requests).Arguments);
        Assert.Equal(new CilOperand.ConstantI4(35), result.Instructions[3].Operand);
        var instance = method with { Definition = method.Definition with { IsStatic = false } };
        body = Body(Instruction(0, CilOperation.Call, new CilOperand.MethodInstance(instance)), Instruction(1, CilOperation.Return));
        Assert.Same(body, AsRule(new ConstantCallRewriter(Graph(body), new MethodFacts(), evaluator, Operands(new MethodFacts()), Loads(new MethodFacts()))).Rewrite(body));
        Assert.Single(evaluator.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacesOnlyProvenConstantCalls(bool nullResult)
    {
        var method = Method();
        var body = Body(
            Instruction(0, CilOperation.LoadNull),
            Instruction(1, CilOperation.LoadNull),
            Instruction(2, CilOperation.Call, new CilOperand.MethodInstance(method)),
            Instruction(3, CilOperation.Return));
        var evaluator = new RecordingEvaluator(nullResult ? new ScalarConstant.Null() : new ScalarConstant.Int32(1));
        var rule = AsRule(new ConstantCallRewriter(Graph(body), new MethodFacts(), evaluator, Operands(new MethodFacts()), Loads(new MethodFacts())));

        var result = rule.Rewrite(body);

        Assert.Collection(Assert.Single(evaluator.Requests).Arguments,
            value => Assert.IsType<ScalarConstant.Null>(value),
            value => Assert.IsType<ScalarConstant.Null>(value));
        Assert.Equal(CilOperation.Nop, result.Instructions[0].Operation);
        Assert.Equal(CilOperation.Nop, result.Instructions[1].Operation);
        Assert.Equal(nullResult ? CilOperation.LoadNull : CilOperation.LoadInt32, result.Instructions[2].Operation);
        Assert.Equal(body.Instructions[2].SourceOffset, result.Instructions[2].SourceOffset);
        Assert.Equal(body.Instructions[3], result.Instructions[3]);
    }

    [Fact]
    public void KeepsUnknownResultsAndEffectfulArgumentProducers()
    {
        var method = Method();
        var unknownBody = Body(
            Instruction(0, CilOperation.LoadNull),
            Instruction(1, CilOperation.LoadNull),
            Instruction(2, CilOperation.Call, new CilOperand.MethodInstance(method)),
            Instruction(3, CilOperation.Return));
        var unknown = new RecordingEvaluator(null);
        var unknownRule = AsRule(new ConstantCallRewriter(Graph(unknownBody), new MethodFacts(), unknown, Operands(new MethodFacts()), Loads(new MethodFacts())));
        Assert.Same(unknownBody, unknownRule.Rewrite(unknownBody));
        Assert.Single(unknown.Requests);

        var effectful = Body(
            Instruction(0, CilOperation.LoadNull),
            Instruction(1, CilOperation.LoadStaticField),
            Instruction(2, CilOperation.Call, new CilOperand.MethodInstance(method)),
            Instruction(3, CilOperation.Return));
        var forbidden = new RecordingEvaluator(new ScalarConstant.Int32(1));
        var effectfulRule = AsRule(new ConstantCallRewriter(Graph(effectful), new MethodFacts(), forbidden, Operands(new MethodFacts()), Loads(new MethodFacts())));
        Assert.Same(effectful, effectfulRule.Rewrite(effectful));
        Assert.Empty(forbidden.Requests);
    }

    // An argument a load without effects pushes reaches the evaluator as unknown; where the result does not depend on it,
    // the call folds and the load goes (a static helper whose value a stubbed check decides).
    [Fact]
    public void FoldsCallsWhoseResultDoesNotDependOnAnUnknownArgument()
    {
        var method = Method();
        var body = Body(
            Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(1, CilOperation.LoadNull),
            Instruction(2, CilOperation.Call, new CilOperand.MethodInstance(method)),
            Instruction(3, CilOperation.Return));
        var evaluator = new RecordingEvaluator(new ScalarConstant.Int32(0));
        var rule = AsRule(new ConstantCallRewriter(Graph(body), new MethodFacts(), evaluator, Operands(new MethodFacts()), Loads(new MethodFacts())));

        var result = rule.Rewrite(body);

        Assert.Collection(Assert.Single(evaluator.Requests).Arguments,
            value => Assert.Null(value),
            value => Assert.IsType<ScalarConstant.Null>(value));
        Assert.Equal([CilOperation.Nop, CilOperation.Nop, CilOperation.LoadInt32, CilOperation.Return],
            result.Instructions.Select(instruction => instruction.Operation));
    }

    [Fact]
    public void DoesNotInferArgumentsAcrossAControlFlowJoin()
    {
        var method = Method();
        var body = Body(
            Instruction(0, CilOperation.LoadNull),
            Instruction(1, CilOperation.LoadNull),
            Instruction(2, CilOperation.Branch, new CilOperand.BranchTarget(3)),
            Instruction(3, CilOperation.Call, new CilOperand.MethodInstance(method)),
            Instruction(4, CilOperation.Return));
        var evaluator = new RecordingEvaluator(new ScalarConstant.Int32(1));
        var rule = AsRule(new ConstantCallRewriter(Graph(body), new MethodFacts(), evaluator, Operands(new MethodFacts()), Loads(new MethodFacts())));

        Assert.Same(body, rule.Rewrite(body));
        Assert.Empty(evaluator.Requests);
    }

    [Fact]
    public void LeavesBodiesWithoutCallsUntouched()
    {
        var body = Body(Instruction(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), Instruction(1, CilOperation.Return));
        var evaluator = new RecordingEvaluator(new ScalarConstant.Int32(1));

        Assert.Same(body, AsRule(new ConstantCallRewriter(new UnusedGraphs(), new MethodFacts(), evaluator, Operands(new MethodFacts()), Loads(new MethodFacts()))).Rewrite(body));
        Assert.Empty(evaluator.Requests);
    }

    [Fact]
    public void FoldsAnInstanceComparisonOnALiteralReceiver()
    {
        var body = Body(
            Instruction(0, CilOperation.LoadString, new CilOperand.UserString("a")),
            Instruction(1, CilOperation.LoadString, new CilOperand.UserString("b")),
            Instruction(2, CilOperation.CallVirtual, new CilOperand.MethodInstance(StringMethod("Equals", false, Text))),
            Instruction(3, CilOperation.Return));
        var evaluator = new RecordingEvaluator(new ScalarConstant.Int32(0));

        var result = AsRule(new ConstantCallRewriter(Graph(body), new MethodFacts(), evaluator, Operands(new MethodFacts()), Loads(new MethodFacts()))).Rewrite(body);

        Assert.Equal(new ScalarConstant?[] { Literal("a"), Literal("b") }, Assert.Single(evaluator.Requests).Arguments);
        Assert.Equal([CilOperation.Nop, CilOperation.Nop, CilOperation.LoadInt32, CilOperation.Return],
            result.Instructions.Select(instruction => instruction.Operation));
    }

    [Fact]
    public void SkipsAVirtualCallToAStaticTarget()
    {
        var body = Body(
            Instruction(0, CilOperation.LoadNull),
            Instruction(1, CilOperation.CallVirtual, new CilOperand.MethodInstance(Method())),
            Instruction(2, CilOperation.Return));
        var evaluator = new RecordingEvaluator(new ScalarConstant.Int32(0));

        Assert.Same(body, AsRule(new ConstantCallRewriter(Graph(body), new MethodFacts(), evaluator, Operands(new MethodFacts()), Loads(new MethodFacts()))).Rewrite(body));
        Assert.Empty(evaluator.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsAUtf8LiteralArgumentAndReplacesWithTheResult(bool stringResult)
    {
        var facts = Facts();
        var signature = new MethodSignatureModel(CliTypeIdentity.FromStackKind(CliValueKind.I4), [Span(Byte)]);
        var method = Method() with { Signature = signature };
        method = method with { Definition = method.Definition with { Signature = signature } };
        var body = Body([.. Utf8Literal(3), Instruction(3, CilOperation.Call, new CilOperand.MethodInstance(method)), Instruction(4, CilOperation.Return)]);
        var evaluator = new RecordingEvaluator(stringResult ? Literal("x") : new ScalarConstant.Int32(1));

        var result = AsRule(new ConstantCallRewriter(Graph(body), facts, evaluator, Operands(facts), Loads(facts))).Rewrite(body);

        Assert.Equal(new ScalarConstant?[] { new ScalarConstant.Bytes([80, 78, 71]) }, Assert.Single(evaluator.Requests).Arguments);
        Assert.All(result.Instructions.Take(3), instruction => Assert.Equal(CilOperation.Nop, instruction.Operation));
        Assert.Equal(stringResult ? new CilOperand.UserString("x") : new CilOperand.ConstantI4(1), result.Instructions[3].Operand);
    }

    [Fact]
    public void KeepsCallsWhoseResultHasNoLiteralForm()
    {
        var body = Body(Instruction(0, CilOperation.Call, new CilOperand.MethodInstance(Method() with
        {
            Signature = MethodSignatureModel.Create(CliValueKind.I4),
        })), Instruction(1, CilOperation.Return));
        var evaluator = new RecordingEvaluator(new ScalarConstant.Bytes([1]));

        Assert.Same(body, AsRule(new ConstantCallRewriter(Graph(body), new MethodFacts(), evaluator, Operands(new MethodFacts()), Loads(new MethodFacts()))).Rewrite(body));
        Assert.Single(evaluator.Requests);
    }

    [Fact]
    public void RequiresEveryArgumentToBeALiteralOrALoadWithoutEffects()
    {
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4, CliValueKind.I4);
        var method = Method() with { Signature = signature };
        // The first argument is a call's result: neither a literal nor a load without effects.
        var body = Body(
            Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(1, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(2, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(3, CilOperation.Call),
            Instruction(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            Instruction(5, CilOperation.Call, new CilOperand.MethodInstance(method)),
            Instruction(6, CilOperation.Return));
        var evaluator = new RecordingEvaluator(new ScalarConstant.Int32(1));

        Assert.Same(body, AsRule(new ConstantCallRewriter(Graph(body), new MethodFacts(), evaluator, Operands(new MethodFacts()), Loads(new MethodFacts()))).Rewrite(body));
        Assert.Empty(evaluator.Requests);
    }

    private sealed class UnusedGraphs : NetWasm.Compiler.ControlFlow.IControlFlowGraphBuilder
    {
        public NetWasm.Compiler.ControlFlow.ControlFlowGraph Build(CilMethodBody body) => throw new InvalidOperationException("A body without calls needs no graph.");
    }

    private static IMethodRewriteRule AsRule(IMethodRewriteRule rule) => rule;

    private sealed class RecordingEvaluator(ScalarConstant? result) : IConstantCallEvaluator
    {
        public List<ConstantCallEvaluation> Requests { get; } = [];
        public ScalarConstant? Evaluate(ConstantCallEvaluation request)
        {
            Requests.Add(request);
            return result;
        }
    }
}
