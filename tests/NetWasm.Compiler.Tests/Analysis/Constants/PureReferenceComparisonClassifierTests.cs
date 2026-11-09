using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

using static ConstantRewriteTestSupport;

public sealed class PureReferenceComparisonClassifierTests
{
    [Fact]
    public void RejectsMissingDependenciesAndMethod()
    {
        var facts = new MethodFacts();
        Assert.Throws<ArgumentNullException>(() => new PureReferenceComparisonClassifier(null!, facts, facts, facts, facts.ReturnEpilogues, facts));
        Assert.Throws<ArgumentNullException>(() => new PureReferenceComparisonClassifier(facts, null!, facts, facts, facts.ReturnEpilogues, facts));
        Assert.Throws<ArgumentNullException>(() => new PureReferenceComparisonClassifier(facts, facts, null!, facts, facts.ReturnEpilogues, facts));
        Assert.Throws<ArgumentNullException>(() => new PureReferenceComparisonClassifier(facts, facts, facts, null!, facts.ReturnEpilogues, facts));
        Assert.Throws<ArgumentNullException>(() => new PureReferenceComparisonClassifier(facts, facts, facts, facts, null!, facts));
        Assert.Throws<ArgumentNullException>(() => new PureReferenceComparisonClassifier(facts, facts, facts, facts, facts.ReturnEpilogues, null!));
        Assert.Throws<ArgumentNullException>(() => facts.Classifier().Classify(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ImportedImplementationsAreNotManagedProofs(int kind)
    {
        var method = Method();
        method = method with
        {
            Definition = method.Definition with
            {
                JSImport = kind == 0 ? new("compare", "fixture") : null,
                WitImport = kind == 1 ? new("fixture", "compare") : null,
                NativeImport = kind == 2 ? new("fixture", "compare", 0, false, false, false, false) : null,
            }
        };
        Assert.Null(new MethodFacts().Classifier().Classify(method));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void RejectsNonComparisonGrammarAndExceptionHandlers(int kind)
    {
        var facts = new MethodFacts();
        var method = Method();
        var body = ComparisonBody(method, CilOperation.CompareEqual, null, kind is 4 or 5);
        body = kind switch
        {
            0 => body with { ExceptionRegions = [new(CilExceptionRegionKind.Finally, 0, 2, 2, 2, null, null)] },
            1 => body with { Instructions = [] },
            2 => body with { Instructions = body.Instructions.SetItem(0, Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(2))) },
            3 => body with { Instructions = body.Instructions.SetItem(1, Instruction(1, CilOperation.LoadArgument, new CilOperand.Index(0))) },
            4 => body with { Instructions = body.Instructions.SetItem(3, Instruction(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(1))) },
            5 => body with { Instructions = body.Instructions.SetItem(4, Instruction(4, CilOperation.Add)) },
            6 => ComparisonBody(method, CilOperation.Call, new CilOperand.None(), false),
            7 => body with { Instructions = body.Instructions.SetItem(0, Instruction(0, CilOperation.LoadNull)) },
            8 => body with { Instructions = body.Instructions.SetItem(1, Instruction(1, CilOperation.LoadNull)) },
            9 => body with { Instructions = body.Instructions.SetItem(1, Instruction(1, CilOperation.LoadArgument, new CilOperand.Index(2))) },
            _ => body with { Instructions = body.Instructions.SetItem(3, Instruction(3, CilOperation.Throw)) },
        };
        facts.Bodies[method.Definition.Key] = body;

        Assert.Null(facts.Classifier().Classify(method));
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 1)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 0)]
    public void InfersForwardedComparisonsFromBodies(bool invertLeaf, bool invertWrapper, int expected)
    {
        var facts = new MethodFacts();
        var leaf = Method(1);
        var wrapper = Method(2);
        facts.Bodies[leaf.Definition.Key] = ComparisonBody(leaf, CilOperation.CompareEqual, null, invertLeaf);
        facts.Bodies[wrapper.Definition.Key] = ComparisonBody(wrapper, CilOperation.Call, new CilOperand.MethodInstance(leaf), invertWrapper);

        Assert.Equal((ReferenceComparison)expected, facts.Classifier().Classify(wrapper));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void RejectsUnprovenContracts(int scenario)
    {
        var facts = new MethodFacts { HasInitializer = scenario == 6 };
        var method = Method();
        method = method with
        {
            Definition = method.Definition with
            {
                IsStatic = scenario != 0,
                IsVirtual = scenario == 1,
                IsNoInlining = scenario == 2,
                RelativeVirtualAddress = scenario == 3 ? 0 : 1,
            },
            Signature = scenario switch
            {
                4 => MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.ManagedReference, CliValueKind.ManagedReference),
                5 => MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4, CliValueKind.I4),
                _ => method.Signature,
            },
        };
        facts.Bodies[method.Definition.Key] = ComparisonBody(method, CilOperation.CompareEqual, null, false);

        Assert.Null(facts.Classifier().Classify(method));
    }

    [Fact]
    public void BeforeFieldInitOwnersKeepComparisonProofs()
    {
        // System.Type gains field initializers but stays beforefieldinit, so typeof
        // comparisons must still fold.
        var facts = new MethodFacts { HasInitializer = true };
        facts.Definition = facts.Definition with { IsBeforeFieldInit = true };
        var method = Method();
        facts.Bodies[method.Definition.Key] = ComparisonBody(method, CilOperation.CompareEqual, null, false);

        Assert.Equal(ReferenceComparison.Equal, facts.Classifier().Classify(method));
    }

    [Fact]
    public void RejectsRecursiveForwarding()
    {
        var facts = new MethodFacts();
        var method = Method();
        facts.Bodies[method.Definition.Key] = ComparisonBody(method, CilOperation.Call, new CilOperand.MethodInstance(method), false);

        Assert.Null(facts.Classifier().Classify(method));
    }

    [Fact]
    public void RejectsRuntimeIntrinsicBodiesAsComparisonProofs()
    {
        var facts = new MethodFacts { IsIntrinsic = true };
        Assert.Null(facts.Classifier().Classify(Method()));
    }

    [Theory]
    [InlineData(CilOperation.StoreStaticField)]
    [InlineData(CilOperation.CallVirtual)]
    [InlineData(CilOperation.NewObject)]
    public void RejectsEffectfulBodies(CilOperation effect)
    {
        var facts = new MethodFacts();
        var method = Method();
        facts.Bodies[method.Definition.Key] = ComparisonBody(method, effect, null, false);

        Assert.Null(facts.Classifier().Classify(method));
    }

    private static CilMethodBody ComparisonBody(MethodInstanceModel method, CilOperation comparison, CilOperand? operand, bool inverted)
    {
        var instructions = new List<CilInstruction>
        {
            Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(1, CilOperation.LoadArgument, new CilOperand.Index(1)),
            Instruction(2, comparison, operand),
        };
        if (inverted)
        {
            instructions.Add(Instruction(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)));
            instructions.Add(Instruction(4, CilOperation.CompareEqual));
        }
        instructions.Add(Instruction(instructions.Count, CilOperation.Return));
        return new(method.Definition, 2, [], [.. instructions]);
    }
}
