using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

using static ConstantRewriteTestSupport;
using static ConstantOperandReaderTests;
using static ConstantComparisonEvaluatorTests;

public sealed class ConstantCallEvaluatorTests
{
    [Theory]
    [InlineData(CilOperation.BranchIfTrue, 0, 0)]
    [InlineData(CilOperation.BranchIfTrue, -1, 1)]
    [InlineData(CilOperation.BranchIfFalse, 0, 1)]
    [InlineData(CilOperation.BranchIfFalse, 42, 0)]
    public void BranchTruthUsesZeroVersusNonzeroIntegers(CilOperation branch, int value, int expected)
    {
        var facts = new MethodFacts();
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4);
        var method = Method() with { Signature = signature };
        method = method with { Definition = method.Definition with { Signature = signature } };
        facts.Bodies[method.Definition.Key] = new(method.Definition, 1, [],
        [
            Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(1, branch, new CilOperand.BranchTarget(4)),
            Instruction(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            Instruction(3, CilOperation.Return),
            Instruction(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            Instruction(5, CilOperation.Return),
        ]);

        Assert.Equal(new ScalarConstant.Int32(expected), AsEvaluator(Evaluator(facts))
            .Evaluate(new(method, [new ScalarConstant.Int32(value)])));
    }

    [Fact]
    public void UnmodelledConstantKindsCannotBecomeEvaluationProofs()
    {
        var facts = new MethodFacts();
        Assert.Null(AsEvaluator(Evaluator(facts))
            .Evaluate(new(Predicate(), [new UnmodelledConstant()])));
    }

    private sealed record UnmodelledConstant : ScalarConstant;

    [Fact]
    public void RejectsMissingDependencies()
    {
        var facts = new MethodFacts();
        Assert.Throws<ArgumentNullException>(() => new ConstantCallEvaluator(null!, facts, facts, facts, facts, Operands(facts), Comparisons(facts), new NetWasm.Compiler.Analysis.MethodSpecializer([])));
        Assert.Throws<ArgumentNullException>(() => new ConstantCallEvaluator(facts, null!, facts, facts, facts, Operands(facts), Comparisons(facts), new NetWasm.Compiler.Analysis.MethodSpecializer([])));
        Assert.Throws<ArgumentNullException>(() => new ConstantCallEvaluator(facts, facts, null!, facts, facts, Operands(facts), Comparisons(facts), new NetWasm.Compiler.Analysis.MethodSpecializer([])));
        Assert.Throws<ArgumentNullException>(() => new ConstantCallEvaluator(facts, facts, facts, null!, facts, Operands(facts), Comparisons(facts), new NetWasm.Compiler.Analysis.MethodSpecializer([])));
        Assert.Throws<ArgumentNullException>(() => new ConstantCallEvaluator(facts, facts, facts, facts, null!, Operands(facts), Comparisons(facts), new NetWasm.Compiler.Analysis.MethodSpecializer([])));
        Assert.Throws<ArgumentNullException>(() => new ConstantCallEvaluator(facts, facts, facts, facts, facts, null!, Comparisons(facts), new NetWasm.Compiler.Analysis.MethodSpecializer([])));
        Assert.Throws<ArgumentNullException>(() => new ConstantCallEvaluator(facts, facts, facts, facts, facts, Operands(facts), null!, new NetWasm.Compiler.Analysis.MethodSpecializer([])));
        Assert.Throws<ArgumentNullException>(() => new ConstantCallEvaluator(facts, facts, facts, facts, facts, Operands(facts), Comparisons(facts), null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DoesNotEvaluateImportedBodies(int kind)
    {
        var facts = new MethodFacts();
        var method = Predicate();
        method = method with
        {
            Definition = method.Definition with
            {
                JSImport = kind == 0 ? new("predicate", "fixture") : null,
                WitImport = kind == 1 ? new("fixture", "predicate") : null,
                NativeImport = kind == 2 ? new("fixture", "predicate", 0, false, false, false, false) : null,
            }
        };

        Assert.Null(AsEvaluator(Evaluator(facts))
            .Evaluate(new(method, [new ScalarConstant.Null()])));
    }

    [Fact]
    public void ReturnsLiteralNullWithReferenceReturnType()
    {
        var facts = new MethodFacts();
        var signature = MethodSignatureModel.Create(CliValueKind.ManagedReference);
        var method = Predicate() with { Signature = signature };
        method = method with { Definition = method.Definition with { Signature = signature } };
        facts.Bodies[method.Definition.Key] = new(method.Definition, 1, [],
            [Instruction(0, CilOperation.LoadNull), Instruction(1, CilOperation.Return)]);

        Assert.IsType<ScalarConstant.Null>(AsEvaluator(Evaluator(facts))
            .Evaluate(new(method, [])));
    }

    [Theory]
    [InlineData(CilOperation.BranchIfEqual, 7, 7, 1)]
    [InlineData(CilOperation.BranchIfEqual, 7, 8, 0)]
    [InlineData(CilOperation.BranchIfNotEqual, 7, 7, 0)]
    [InlineData(CilOperation.BranchIfNotEqual, 7, 8, 1)]
    public void EvaluatesBothEqualityBranchDirections(CilOperation operation, int left, int right, int expected)
    {
        var facts = new MethodFacts();
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4, CliValueKind.I4);
        var method = Method() with { Signature = signature };
        method = method with { Definition = method.Definition with { Signature = signature } };
        facts.Bodies[method.Definition.Key] = new(method.Definition, 2, [],
        [
            Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(1, CilOperation.LoadArgument, new CilOperand.Index(1)),
            Instruction(2, operation, new CilOperand.BranchTarget(5)),
            Instruction(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            Instruction(4, CilOperation.Return),
            Instruction(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            Instruction(6, CilOperation.Return),
        ]);

        Assert.Equal(new ScalarConstant.Int32(expected),
            AsEvaluator(Evaluator(facts))
                .Evaluate(new(method, [new ScalarConstant.Int32(left), new ScalarConstant.Int32(right)])));
    }

    [Theory]
    [InlineData(CilOperation.Duplicate)]
    [InlineData(CilOperation.Pop)]
    [InlineData(CilOperation.StoreLocal)]
    [InlineData(CilOperation.LoadLocal)]
    [InlineData(CilOperation.CompareEqual)]
    [InlineData(CilOperation.BranchIfTrue)]
    [InlineData(CilOperation.BranchIfEqual)]
    [InlineData(CilOperation.Call)]
    [InlineData(CilOperation.Return)]
    public void IncompleteScalarProofDoesNotProduceAConstant(CilOperation operation)
    {
        var facts = new MethodFacts();
        var method = Predicate();
        var operand = operation switch
        {
            CilOperation.StoreLocal or CilOperation.LoadLocal => (CilOperand)new CilOperand.Index(0),
            CilOperation.BranchIfTrue or CilOperation.BranchIfEqual => new CilOperand.BranchTarget(1),
            CilOperation.Call => new CilOperand.MethodInstance(method),
            _ => new CilOperand.None(),
        };
        facts.Bodies[method.Definition.Key] = new(method.Definition, 1, [CliValueKind.I4],
            [Instruction(0, operation, operand), Instruction(1, CilOperation.Return)]);

        Assert.Null(AsEvaluator(Evaluator(facts))
            .Evaluate(new(method, [new ScalarConstant.Null()])));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyBodiesAndMissingNextInstructionsAreNotFiniteProofs(bool empty)
    {
        var facts = new MethodFacts();
        var method = Predicate();
        facts.Bodies[method.Definition.Key] = new(method.Definition, 1, [],
            empty ? [] : [Instruction(0, CilOperation.Nop)]);

        Assert.Null(AsEvaluator(Evaluator(facts))
            .Evaluate(new(method, [new ScalarConstant.Null()])));
    }

    [Fact]
    public void ProvesNullPredicateWithoutTraversingTheEffectfulAlternative()
    {
        var facts = new MethodFacts();
        var method = Predicate();
        facts.Bodies[method.Definition.Key] = NullPredicate(method);
        var evaluator = AsEvaluator(Evaluator(facts));

        Assert.Equal(new ScalarConstant.Int32(1), evaluator.Evaluate(new(method, [new ScalarConstant.Null()])));
        Assert.Null(evaluator.Evaluate(new(method, [null])));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreservesExplicitInitializationButAllowsEffectFreeBeforeFieldInitPaths(bool beforeFieldInit)
    {
        var facts = new MethodFacts { HasInitializer = true };
        facts.Definition = facts.Definition with { IsBeforeFieldInit = beforeFieldInit };
        var method = Predicate();
        facts.Bodies[method.Definition.Key] = NullPredicate(method);
        var evaluator = AsEvaluator(Evaluator(facts));

        Assert.Equal(beforeFieldInit ? new ScalarConstant.Int32(1) : null,
            evaluator.Evaluate(new(method, [new ScalarConstant.Null()])));
    }

    [Theory]
    [InlineData(CilOperation.StoreStaticField)]
    [InlineData(CilOperation.LoadStaticField)]
    [InlineData(CilOperation.NewObject)]
    [InlineData(CilOperation.LoadArgumentAddress)]
    [InlineData(CilOperation.Throw)]
    public void DoesNotFoldExecutedEffects(CilOperation operation)
    {
        var facts = new MethodFacts();
        var method = Predicate();
        facts.Bodies[method.Definition.Key] = new(method.Definition, 1, [],
            [Instruction(0, operation), Instruction(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), Instruction(2, CilOperation.Return)]);
        var evaluator = AsEvaluator(Evaluator(facts));

        Assert.Null(evaluator.Evaluate(new(method, [new ScalarConstant.Null()])));
    }

    [Fact]
    public void ProvesForwardedPredicatesAndDoesNotLeakRecursionStateBetweenCalls()
    {
        var facts = new MethodFacts();
        var leaf = Predicate();
        var wrapper = Predicate(2);
        facts.Bodies[leaf.Definition.Key] = NullPredicate(leaf);
        facts.Bodies[wrapper.Definition.Key] = new(wrapper.Definition, 1, [],
        [
            Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(1, CilOperation.Call, new CilOperand.MethodInstance(leaf)),
            Instruction(2, CilOperation.Return),
        ]);
        var evaluator = AsEvaluator(Evaluator(facts));

        Assert.Equal(new ScalarConstant.Int32(1), evaluator.Evaluate(new(wrapper, [new ScalarConstant.Null()])));
        Assert.Equal(new ScalarConstant.Int32(1), evaluator.Evaluate(new(wrapper, [new ScalarConstant.Null()])));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeepsRecursiveCallsAndCyclicPaths(bool recursiveCall)
    {
        var facts = new MethodFacts();
        var method = Predicate();
        facts.Bodies[method.Definition.Key] = recursiveCall
            ? new(method.Definition, 1, [],
            [
                Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
                Instruction(1, CilOperation.Call, new CilOperand.MethodInstance(method)),
                Instruction(2, CilOperation.Return),
            ])
            : new(method.Definition, 1, [], [Instruction(0, CilOperation.Branch, new CilOperand.BranchTarget(0))]);
        var evaluator = AsEvaluator(Evaluator(facts));

        Assert.Null(evaluator.Evaluate(new(method, [new ScalarConstant.Null()])));
    }

    [Fact]
    public void EvaluatesDeepFiniteCallChainsWithoutUsingTheHostCallStack()
    {
        const int depth = 10_000;
        var facts = new MethodFacts();
        for (var index = 1; index < depth; index++)
        {
            var method = Predicate(index);
            facts.Bodies[method.Definition.Key] = new(method.Definition, 1, [],
            [
                Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
                Instruction(1, CilOperation.Call, new CilOperand.MethodInstance(Predicate(index + 1))),
                Instruction(2, CilOperation.Return),
            ]);
        }
        var leaf = Predicate(depth);
        facts.Bodies[leaf.Definition.Key] = NullPredicate(leaf);
        var evaluator = AsEvaluator(Evaluator(facts));

        Assert.Equal(new ScalarConstant.Int32(1), evaluator.Evaluate(new(Predicate(), [new ScalarConstant.Null()])));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void KeepsUnprovenCallBoundaries(int scenario)
    {
        var facts = new MethodFacts();
        var method = Predicate();
        method = method with
        {
            Definition = method.Definition with
            {
                IsStatic = scenario != 0,
                IsVirtual = scenario == 1,
                IsNoInlining = scenario == 2,
                RelativeVirtualAddress = scenario == 3 ? 0 : 1,
            },
            Signature = scenario == 4 ? MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.ManagedReference) : method.Signature,
        };
        facts.Bodies[method.Definition.Key] = NullPredicate(method);
        var evaluator = AsEvaluator(Evaluator(facts));
        // An instance request carries its receiver first.
        ScalarConstant?[] arguments = scenario == 0
            ? [new ScalarConstant.String("receiver"), new ScalarConstant.Null()]
            : [new ScalarConstant.Null()];

        Assert.Null(evaluator.Evaluate(new(method, [.. arguments])));
    }

    [Fact]
    public void PreservesExceptionRegionsAndUnknownArgumentKinds()
    {
        var facts = new MethodFacts();
        var method = Predicate();
        facts.Bodies[method.Definition.Key] = NullPredicate(method) with
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Finally, 0, 2, 2, 2, null, null)],
        };
        var evaluator = AsEvaluator(Evaluator(facts));

        Assert.Null(evaluator.Evaluate(new(method, [new ScalarConstant.Null()])));
        Assert.Null(evaluator.Evaluate(new(method, [new ScalarConstant.Int32(0)])));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    public void TracksCopiedScalarValuesAndEquality(int value)
    {
        var facts = new MethodFacts();
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4);
        var method = Predicate() with { Signature = signature };
        method = method with { Definition = method.Definition with { Signature = signature } };
        facts.Bodies[method.Definition.Key] = new(method.Definition, 2, [CliValueKind.I4],
        [
            Instruction(0, CilOperation.Nop),
            Instruction(1, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(2, CilOperation.Duplicate),
            Instruction(3, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(4, CilOperation.Pop),
            Instruction(5, CilOperation.LoadLocal, new CilOperand.Index(0)),
            Instruction(6, CilOperation.LoadInt32, new CilOperand.ConstantI4(value)),
            Instruction(7, CilOperation.CompareEqual),
            Instruction(8, CilOperation.Return),
        ]);
        var evaluator = AsEvaluator(Evaluator(facts));

        Assert.Equal(new ScalarConstant.Int32(1), evaluator.Evaluate(new(method, [new ScalarConstant.Int32(value)])));
    }

    [Fact]
    public void ValidatesEvaluationInputsBeforeReadingBodies()
    {
        var facts = new MethodFacts();
        var evaluator = AsEvaluator(Evaluator(facts));

        Assert.Throws<ArgumentNullException>(() => evaluator.Evaluate(null!));
        Assert.Throws<ArgumentNullException>(() => evaluator.Evaluate(new(null!, [])));
        Assert.Throws<ArgumentException>(() => evaluator.Evaluate(new(Predicate(), [])));
        Assert.Throws<ArgumentException>(() => evaluator.Evaluate(new(Predicate(), default)));
    }

    [Fact]
    public void DoesNotReadAnIntrinsicPlaceholderBody()
    {
        var facts = new MethodFacts { IsIntrinsic = true };
        var evaluator = AsEvaluator(Evaluator(facts));
        Assert.Null(evaluator.Evaluate(new(Predicate(), [new ScalarConstant.Null()])));
    }

    [Fact]
    public void FoldsCoreComparisonsWithoutReadingABody()
    {
        var facts = new MethodFacts();
        var evaluator = AsEvaluator(Evaluator(facts));
        Assert.Equal(new ScalarConstant.Int32(1), evaluator.Evaluate(new(StringMethod("op_Equality", true, Text, Text),
            [Literal("a"), Literal("a")])));

        // An unknown argument cannot be compared, so the method's own body decides.
        var method = Predicate();
        facts.Bodies[method.Definition.Key] = new(method.Definition, 1, [],
            [Instruction(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), Instruction(1, CilOperation.Return)]);
        Assert.Equal(new ScalarConstant.Int32(1), evaluator.Evaluate(new(method, [null])));
    }

    [Theory]
    [InlineData(false, "Admin", 1)]
    [InlineData(false, "Guest", 0)]
    [InlineData(true, "Admin", 1)]
    [InlineData(true, "Guest", 0)]
    public void EvaluatesStringComparisonsInsideAHelper(bool instance, string role, int expected)
    {
        var facts = new MethodFacts();
        var method = Predicate();
        var comparison = instance ? StringMethod("Equals", false, Text) : StringMethod("op_Equality", true, Text, Text);
        facts.Bodies[method.Definition.Key] = new(method.Definition, 2, [],
        [
            Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(1, CilOperation.LoadString, new CilOperand.UserString("Admin")),
            Instruction(2, instance ? CilOperation.CallVirtual : CilOperation.Call, new CilOperand.MethodInstance(comparison)),
            Instruction(3, CilOperation.Return),
        ]);

        Assert.Equal(new ScalarConstant.Int32(expected), AsEvaluator(Evaluator(facts)).Evaluate(new(method, [Literal(role)])));
    }

    [Theory]
    [InlineData(new byte[] { 80, 78, 71 }, 1)]
    [InlineData(new byte[] { 71, 73, 70 }, 0)]
    public void EvaluatesUtf8LiteralComparisonsInsideAHelper(byte[] header, int expected)
    {
        var facts = Facts();
        var method = SpanPredicate(Span(Byte));
        facts.Bodies[method.Definition.Key] = new(method.Definition, 3, [],
        [
            Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            .. Utf8Literal(3, 1),
            Instruction(4, CilOperation.Call, new CilOperand.MethodInstance(SequenceEqual([Span(Byte), Span(Byte)], [Byte]))),
            Instruction(5, CilOperation.Return),
        ]);
        var evaluator = AsEvaluator(Evaluator(facts));

        Assert.Equal(new ScalarConstant.Int32(expected), evaluator.Evaluate(new(method, [new ScalarConstant.Bytes([.. header])])));
        Assert.Null(evaluator.Evaluate(new(SpanPredicate(Span(CliTypeIdentity.Primitive("i4", CliValueKind.I4))), [new ScalarConstant.Bytes([1])])));
        Assert.Null(evaluator.Evaluate(new(SpanPredicate(CliTypeIdentity.FromStackKind(CliValueKind.I4)), [new ScalarConstant.Bytes([1])])));
    }

    [Fact]
    public void DeclinesAnUnreadableProducer()
    {
        var facts = Facts();
        var method = Method() with { Signature = MethodSignatureModel.Create(CliValueKind.I4) };
        method = method with { Definition = method.Definition with { Signature = method.Signature } };
        facts.Bodies[method.Definition.Key] = new(method.Definition, 2, [],
            [Instruction(0, CilOperation.LoadStaticFieldAddress, new CilOperand.Entity(FieldKey)), Instruction(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(3))]);

        Assert.Null(AsEvaluator(Evaluator(facts)).Evaluate(new(method, [])));
    }

    [Theory]
    [InlineData(CilOperation.CompareEqual, null, null, 1)]
    [InlineData(CilOperation.CompareEqual, "a", "b", 0)]
    [InlineData(CilOperation.CompareEqual, "a", null, 0)]
    [InlineData(CilOperation.CompareEqual, null, "a", 0)]
    [InlineData(CilOperation.CompareEqual, "a", "a", null)]
    [InlineData(CilOperation.BranchIfEqual, "a", "b", 0)]
    [InlineData(CilOperation.BranchIfEqual, "a", "a", null)]
    public void ComparesLiteralIdentityOnlyWhenContentsDiffer(CilOperation operation, string? left, string? right, int? expected)
    {
        var facts = new MethodFacts();
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.ManagedReference, CliValueKind.ManagedReference);
        var method = Method() with { Signature = signature };
        method = method with { Definition = method.Definition with { Signature = signature } };
        facts.Bodies[method.Definition.Key] = new(method.Definition, 2, [], operation == CilOperation.CompareEqual
            ? [
                Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
                Instruction(1, CilOperation.LoadArgument, new CilOperand.Index(1)),
                Instruction(2, operation),
                Instruction(3, CilOperation.Return),
            ]
            : [
                Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
                Instruction(1, CilOperation.LoadArgument, new CilOperand.Index(1)),
                Instruction(2, operation, new CilOperand.BranchTarget(5)),
                Instruction(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
                Instruction(4, CilOperation.Return),
                Instruction(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                Instruction(6, CilOperation.Return),
            ]);

        var result = AsEvaluator(Evaluator(facts)).Evaluate(new(method, [Literal(left), Literal(right)]));

        Assert.Equal(expected is { } value ? new ScalarConstant.Int32(value) : null, result);
    }

    [Fact]
    public void DeclinesIdentityBetweenAnIntegerAndAReference()
    {
        var facts = new MethodFacts();
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4, CliValueKind.ManagedReference);
        var method = Method() with { Signature = signature };
        method = method with { Definition = method.Definition with { Signature = signature } };
        facts.Bodies[method.Definition.Key] = new(method.Definition, 2, [],
        [
            Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(1, CilOperation.LoadArgument, new CilOperand.Index(1)),
            Instruction(2, CilOperation.CompareEqual),
            Instruction(3, CilOperation.Return),
        ]);

        Assert.Null(AsEvaluator(Evaluator(facts)).Evaluate(new(method, [new ScalarConstant.Int32(1), new ScalarConstant.Null()])));
    }

    [Fact]
    public void TreatsALiteralAsTrueAndDeclinesASpanCondition()
    {
        var facts = new MethodFacts();
        ImmutableArray<CilInstruction> Branch() =>
        [
            Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(4)),
            Instruction(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            Instruction(3, CilOperation.Return),
            Instruction(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            Instruction(5, CilOperation.Return),
        ];
        var literal = Predicate();
        facts.Bodies[literal.Definition.Key] = new(literal.Definition, 1, [], Branch());
        var span = SpanPredicate(Span(Byte), token: 2);
        facts.Bodies[span.Definition.Key] = new(span.Definition, 1, [], Branch());
        var evaluator = AsEvaluator(Evaluator(facts));

        Assert.Equal(new ScalarConstant.Int32(1), evaluator.Evaluate(new(literal, [Literal("a")])));
        Assert.Null(evaluator.Evaluate(new(span, [new ScalarConstant.Bytes([1])])));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void DeclinesUnprovenNestedCalls(int scenario)
    {
        var facts = new MethodFacts();
        var method = Predicate();
        var instance = Predicate(2);
        instance = instance with { Definition = instance.Definition with { IsStatic = false } };
        var callee = Predicate(3);
        CilInstruction[] instructions = scenario switch
        {
            0 => [Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)), Instruction(1, CilOperation.Call)],
            1 => [Instruction(0, CilOperation.Call, new CilOperand.MethodInstance(callee))],
            2 => [
                Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
                Instruction(1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                Instruction(2, CilOperation.CallVirtual, new CilOperand.MethodInstance(instance)),
            ],
            _ => [
                Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
                Instruction(1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                Instruction(2, CilOperation.Call, new CilOperand.MethodInstance(instance)),
            ],
        };
        facts.Bodies[method.Definition.Key] = new(method.Definition, 2, [], [.. instructions, Instruction(instructions.Length, CilOperation.Return)]);

        Assert.Null(AsEvaluator(Evaluator(facts)).Evaluate(new(method, [Literal("a")])));
    }

    [Fact]
    public void DeclinesAnArgumentBeyondTheSignature()
    {
        var facts = new MethodFacts();
        var method = Predicate();
        facts.Bodies[method.Definition.Key] = new(method.Definition, 1, [],
            [Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(1)), Instruction(1, CilOperation.Return)]);

        Assert.Null(AsEvaluator(Evaluator(facts)).Evaluate(new(method, [new ScalarConstant.Null()])));
    }

    [Fact]
    public void ReturnsAStringLiteral()
    {
        var facts = new MethodFacts();
        var signature = MethodSignatureModel.Create(CliValueKind.ManagedReference);
        var method = Method() with { Signature = signature };
        method = method with { Definition = method.Definition with { Signature = signature } };
        facts.Bodies[method.Definition.Key] = new(method.Definition, 1, [],
            [Instruction(0, CilOperation.LoadString, new CilOperand.UserString("x")), Instruction(1, CilOperation.Return)]);

        Assert.Equal(new ScalarConstant.String("x"), AsEvaluator(Evaluator(facts)).Evaluate(new(method, [])));
    }

    private static MethodInstanceModel SpanPredicate(CliTypeIdentity parameter, int token = 1)
    {
        var signature = new MethodSignatureModel(CliTypeIdentity.FromStackKind(CliValueKind.I4), [parameter]);
        var method = Method(token);
        return method with { Definition = method.Definition with { Signature = signature }, Signature = signature };
    }

    private static IConstantCallEvaluator AsEvaluator(IConstantCallEvaluator evaluator) => evaluator;

    private static MethodInstanceModel Predicate(int token = 1)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.ManagedReference);
        var method = Method(token);
        return method with { Definition = method.Definition with { Signature = signature }, Signature = signature };
    }

    private static CilMethodBody NullPredicate(MethodInstanceModel method) => new(method.Definition, 1, [],
    [
        Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
        Instruction(1, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(5)),
        Instruction(2, CilOperation.StoreStaticField),
        Instruction(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
        Instruction(4, CilOperation.Return),
        Instruction(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
        Instruction(6, CilOperation.Return),
    ]);
}
