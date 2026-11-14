using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Attributes;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Tests;

public sealed class AttributeQueryLowererTests
{
    private static readonly AssemblyIdentity Assembly = new("Lowering");
    private static readonly CliTypeIdentity Target = CliTypeIdentity.Named(Assembly, "Test", "Target", false);
    private static readonly CliTypeIdentity Filter = CliTypeIdentity.Named(Assembly, "Test", "Marker", false);
    private static readonly MethodInstanceModel QueryMethod = Method("Query", 1);
    private static readonly MethodInstanceModel OrdinaryMethod = Method("Ordinary", 2);
    private static readonly MethodInstanceModel AmbiguityConstructor = Method(".ctor", 3);

    [Fact]
    public void BoundedCallsForwardProofsAndPlansAndReserveDisjointLocalSlots()
    {
        var fixture = new Fixture();
        var result = fixture.Lower();

        Assert.Same(fixture.Output, result);
        Assert.Equal([1, 3], fixture.Requests.Select(request => request.FirstLocal));
        Assert.All(fixture.Requests, request =>
        {
            Assert.Equal(AttributeQueryOperation.GetMany, request.Operation);
            Assert.Same(fixture.Selection, request.Selection);
            Assert.Equal(2, request.ArgumentCount);
            Assert.Equal(1, request.InheritArgument);
            Assert.Null(request.InheritConstant);
            Assert.Equal(CliTypeIdentity.SzArray(Filter), request.ResultArrayType);
            Assert.Same(AmbiguityConstructor, request.AmbiguityConstructor);
        });
        Assert.NotNull(fixture.Replacements);
        Assert.Equal([1, 3], fixture.Replacements.Keys.Order());
        Assert.All(fixture.Replacements.Values, fragment => Assert.Same(fixture.Fragment, fragment));
        Assert.Equal(["graph", "stacks", "proofs", "resolve", "plan", "fragment",
            "resolve", "plan", "fragment", "rewrite"], fixture.Stages);
    }

    [Fact]
    public void BodiesWithoutQueriesAreReturnedWithoutControlFlowAnalysis()
    {
        var fixture = new Fixture { RecognizeQueries = false };
        Assert.Same(fixture.Body, fixture.Lower());
        Assert.Empty(fixture.Stages);
        Assert.Null(fixture.Replacements);
    }

    [Theory]
    [InlineData("classify")]
    [InlineData("resolve")]
    [InlineData("plan")]
    [InlineData("fragment")]
    public void QueryFailuresRetainDiagnosticAndOriginalCallerLocation(string failureStage)
    {
        var fixture = new Fixture { FailureStage = failureStage };
        var exception = Assert.Throws<CompilerException>(() => fixture.Lower());
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Equal("Rejected query", exception.Diagnostic.Message);
        Assert.Equal("Test.Target.Caller", exception.Diagnostic.Method);
        Assert.Equal(101, exception.Diagnostic.IlOffset);
        Assert.Null(fixture.Replacements);
        Assert.DoesNotContain("rewrite", fixture.Stages);
        if (failureStage == "classify") Assert.Empty(fixture.Stages);
        else Assert.Equal(failureStage, fixture.Stages[^1]);
    }

    [Theory]
    [InlineData(CilOperation.LoadFunction)]
    [InlineData(CilOperation.LoadVirtualFunction)]
    [InlineData(CilOperation.LoadMethodToken)]
    public void QueryHandlesRetainManagedBodiesWhileDirectCallsAreLowered(CilOperation operation)
    {
        var fixture = new Fixture(operation);
        Assert.Same(fixture.Output, fixture.Lower());
        Assert.NotNull(fixture.Replacements);
        Assert.Equal([3], fixture.Replacements.Keys);
        Assert.Single(fixture.Requests);
    }

    [Fact]
    public void OrdinaryMethodTokensRemainAvailableToOtherCompilerFeatures()
    {
        var fixture = new Fixture(CilOperation.LoadMethodToken) { RecognizeQueries = false };
        Assert.Same(fixture.Body, fixture.Lower());
        Assert.Empty(fixture.Stages);
    }

    [Fact]
    public void MissingProofLeavesManagedCallsWithoutResolutionOrRewriting()
    {
        var fixture = new Fixture { HasProof = false };
        Assert.Same(fixture.Body, fixture.Lower());
        Assert.Equal(["graph", "stacks", "proofs"], fixture.Stages);
        Assert.Null(fixture.Replacements);
    }

    [Fact]
    public void IneligibleQueriesRetainManagedCallsWithoutPlanning()
    {
        var fixture = new Fixture { Eligible = false };
        Assert.Same(fixture.Body, fixture.Lower());
        Assert.Equal(["graph", "stacks", "proofs", "resolve", "resolve"], fixture.Stages);
        Assert.Null(fixture.Replacements);
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public void CallsWithoutResolvedMethodsRemainUnchanged()
    {
        var fixture = new Fixture { ResolveMethods = false };
        Assert.Same(fixture.Body, fixture.Lower());
        Assert.Empty(fixture.Stages);
    }

    [Fact]
    public void NullBodyFailsBeforeCollaboratorsRun()
    {
        var fixture = new Fixture();
        Assert.Throws<ArgumentNullException>(() => fixture.Create().Lower(null!));
        Assert.Empty(fixture.Stages);
    }

    private static MethodInstanceModel Method(string name, int token)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.Void);
        return new(new(new(Assembly, token), new(Assembly, 0x02000001), name, true, signature, 1),
            Target, [], signature);
    }

    private sealed class Fixture : ICalledMethodResolver, IAttributeQueryClassifier,
        IControlFlowGraphBuilder, ITypedStackValidator, IAttributeProvenanceAnalyzer,
        IAttributeQueryResolver, IAttributeQueryPlanner, IAttributeQueryCilBuilder,
        IAttributeQueryBodyRewriter, ISymbolFormatter
    {
        private readonly AttributeQueryCall _call = new(AttributeQueryOperation.GetMany, 2, null, Filter, 1);
        private readonly ImmutableArray<AttributeValueProof> _proof = [new(AttributeValueKind.Type, Target), default];
        private readonly ResolvedAttributeQuery _query;
        private readonly ControlFlowGraph _graph;
        private readonly ValidatedControlFlowGraph _validated;

        internal Fixture(CilOperation operation = CilOperation.Call)
        {
            Body = new(Method("Caller", 4).Definition, 2, [CliValueKind.I4],
                [new(0, 1, CilOperation.Nop, new CilOperand.None()),
                 new(1, 2, operation, operation == CilOperation.LoadMethodToken
                     ? new CilOperand.MethodInstance(QueryMethod) : new CilOperand.None()) { OriginalOffset = 101 },
                 new(2, 3, CilOperation.Call, new CilOperand.None()),
                 new(3, 4, CilOperation.CallVirtual, new CilOperand.None()),
                 new(4, 5, CilOperation.Return, new CilOperand.None())]);
            Output = Body with { MaxStack = 3 };
            _graph = new ControlFlowGraphBuilderFactory().Create().Build(Body);
            _validated = new(_graph, ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
                ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty);
            _query = new(_call, Target, Filter, null);
        }

        internal CilMethodBody Body { get; }
        internal CilMethodBody Output { get; }
        internal bool RecognizeQueries { get; init; } = true;
        internal bool ResolveMethods { get; init; } = true;
        internal bool Eligible { get; init; } = true;
        internal bool HasProof { get; init; } = true;
        internal string? FailureStage { get; init; }
        internal List<string> Stages { get; } = [];
        internal List<AttributeQueryCilRequest> Requests { get; } = [];
        internal AttributeQueryEntry Selection { get; } = new(Target, Filter,
            new AttributeRetrievalResult([]), new AttributeRetrievalResult([]));
        internal AttributeCilFragment Fragment { get; } = new([], [Target, Filter], 2);
        internal ImmutableDictionary<int, AttributeCilFragment>? Replacements { get; private set; }
        internal AttributeQueryLowerer Create() => new(
            this, this, this, this, this, this, this, this, this, this, AmbiguityConstructor);
        internal CilMethodBody Lower() => ((IAttributeQueryLowerer)Create()).Lower(Body);

        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            !ResolveMethods || instruction.Operation == CilOperation.LoadMethodToken ? null : instruction.Offset switch
            {
                1 or 3 => QueryMethod,
                2 => OrdinaryMethod,
                _ => null,
            };

        public AttributeQueryCall? Classify(MethodInstanceModel method)
        {
            if (!RecognizeQueries || method == OrdinaryMethod) return null;
            FailAt("classify");
            return _call;
        }

        public ControlFlowGraph Build(CilMethodBody body)
        {
            Assert.Same(Body, body);
            Stages.Add("graph");
            return _graph;
        }

        public ValidatedControlFlowGraph Validate(ControlFlowGraph graph)
        {
            Assert.Same(_graph, graph);
            Stages.Add("stacks");
            return _validated;
        }

        public ImmutableDictionary<int, ImmutableArray<AttributeValueProof>> Analyze(ValidatedControlFlowGraph controlFlow)
        {
            Assert.Same(_validated, controlFlow);
            Stages.Add("proofs");
            var result = ImmutableDictionary<int, ImmutableArray<AttributeValueProof>>.Empty;
            return HasProof ? result.Add(1, _proof).Add(3, _proof) : result;
        }

        public ResolvedAttributeQuery? Resolve(AttributeQueryCall call, ImmutableArray<AttributeValueProof> stack)
        {
            Assert.Same(_call, call);
            Assert.Equal(_proof, stack);
            Stages.Add("resolve");
            FailAt("resolve");
            return Eligible ? _query : null;
        }

        public AttributeQueryEntry Plan(ResolvedAttributeQuery query)
        {
            Assert.Same(_query, query);
            Stages.Add("plan");
            FailAt("plan");
            return Selection;
        }

        public AttributeCilFragment Build(AttributeQueryCilRequest request)
        {
            Stages.Add("fragment");
            FailAt("fragment");
            Requests.Add(request);
            return Fragment;
        }

        public CilMethodBody Rewrite(CilMethodBody body, ImmutableDictionary<int, AttributeCilFragment> replacements)
        {
            Assert.Same(Body, body);
            Stages.Add("rewrite");
            Replacements = replacements;
            return Output;
        }

        public string Format(EntityKey key) => throw new NotSupportedException();
        public string Format(MethodDefinitionModel method)
        {
            Assert.Same(Body.Method, method);
            return "Test.Target.Caller";
        }

        private void FailAt(string stage)
        {
            if (FailureStage == stage)
                throw new CompilerException(new(DiagnosticCode.UnsupportedMetadata, "Rejected query", "Callee", 7));
        }
    }
}
