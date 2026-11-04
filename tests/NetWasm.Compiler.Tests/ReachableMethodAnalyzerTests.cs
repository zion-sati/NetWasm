using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class ReachableMethodAnalyzerTests
{
    [Fact]
    public void AnalyzerRejectsOriginalBodyBeforeSpecialization()
    {
        var managed = ValidationTestData.ManagedMethodBody();
        var failure = new CompilerException(new(DiagnosticCode.InvalidCil, "invalid original body"));
        var specializer = new RecordingSpecializer();
        var optimizer = new IdentityOptimizer(true);
        var instructions = new RecordingInstructionAnalyzer();
        var graphs = new RecordingGraphBuilder();
        var analyzer = new ReachableMethodAnalyzer(
            new FixedMethodBodyReader(managed.Body),
            new UnusedTypeRepository(), new UnusedFieldRepository(), new UnusedMethodRepository(),
            specializer, optimizer, new RecordingIntegerChecks(), new NoExceptionDiscovery(),
            new FixedStackValidatorFactory(new RejectingStackValidator(failure)), graphs, instructions);

        var exception = Assert.Throws<CompilerException>(() => analyzer.Analyze(new(managed.Method)));

        Assert.Same(failure, exception);
        Assert.Null(specializer.ObservedMethod);
        Assert.Equal(0, optimizer.Calls);
        Assert.False(instructions.Called);
        Assert.Equal(1, graphs.Calls);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void AnalyzerReturnsValidatedManagedBodiesAndInstructionFacts(
        bool rewritten, bool optimizerChanges, bool specializerChanges)
    {
        var assembly = new AssemblyIdentity("in-memory");
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 2),
            new EntityKey(assembly, 1),
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
        var body = new CilMethodBody(
            method,
            1,
            [],
            [
                new CilInstruction(0, 1, CilOperation.Leave, new CilOperand.BranchTarget(2)),
                new CilInstruction(1, 2, CilOperation.Leave, new CilOperand.BranchTarget(2)),
                new CilInstruction(2, 3, CilOperation.Return, new CilOperand.None()),
            ])
        {
            ExceptionRegions =
            [
                new CilExceptionRegion(
                    CilExceptionRegionKind.Catch,
                    0,
                    1,
                    1,
                    1,
                    new EntityKey(assembly, 90),
                    null),
            ],
        };
        var instructionAnalyzer = new RecordingInstructionAnalyzer();
        var graphBuilder = new RecordingGraphBuilder();
        var specializer = new RecordingSpecializer(specializerChanges);
        var optimizer = new IdentityOptimizer(optimizerChanges);
        var integerChecks = new RecordingIntegerChecks();
        var analyzer = new ReachableMethodAnalyzer(
            new FixedMethodBodyReader(body),
            new UnusedTypeRepository(),
            new UnusedFieldRepository(),
            new UnusedMethodRepository(),
            specializer,
            optimizer,
            integerChecks,
            new NoExceptionDiscovery(),
            new FixedStackValidatorFactory(),
            graphBuilder,
            instructionAnalyzer);
        var request = new ReachableMethodRequest(
            new MethodInstanceModel(
                method,
                CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference, false),
                [],
                method.Signature));
        if (rewritten)
        {
            request = request with { RewrittenBody = body with { MethodInstance = request.Method } };
        }

        var result = analyzer.Analyze(request);

        Assert.Same(request.Method, result.Body.Method);
        Assert.Same(request.Method, result.Body.Body.MethodInstance);
        if (rewritten)
        {
            Assert.Null(specializer.ObservedMethod);
            Assert.Same(request.RewrittenBody, result.Body.Body);
            Assert.Equal(0, optimizer.Calls);
            // A rewritten body was optimized before its rewrite; only its integer check proof is found again.
            Assert.Same(request.RewrittenBody, integerChecks.Rewritten);
        }
        else
        {
            Assert.Same(request.Method, specializer.ObservedMethod);
            Assert.Equal(1, optimizer.Calls);
            Assert.Null(integerChecks.Rewritten);
        }
        Assert.Same(graphBuilder.Graph, result.Body.ControlFlow.Graph);
        var rewrites = rewritten ? 0 : (optimizerChanges ? 1 : 0) + (specializerChanges ? 1 : 0);
        Assert.Equal(1 + rewrites, graphBuilder.Calls);
        Assert.Equal(1 + rewrites, result.Body.Body.MaxStack);
        Assert.Contains(new EntityKey(assembly, 90), result.CatchTypes);
        Assert.True(instructionAnalyzer.Called);
    }

    private sealed class FixedMethodBodyReader(CilMethodBody body) : IMethodBodyReader
    {
        public CilMethodBody ReadMethodBody(MethodDefinitionModel method) => body;

        public CilMethodBody ReadMethodBody(MethodInstanceModel method) => body;
    }

    private sealed class RecordingSpecializer(bool changes = false) : IMethodSpecializer
    {
        public MethodInstanceModel? ObservedMethod { get; private set; }

        public CilMethodBody Rewrite(CilMethodBody body)
        {
            ObservedMethod = body.MethodInstance;
            return changes ? body with { MaxStack = body.MaxStack + 1 } : body;
        }
    }

    private sealed class IdentityOptimizer(bool changes) : IManagedMethodOptimizer
    {
        public int Calls { get; private set; }
        public CilMethodBody Optimize(ManagedMethodBody method)
        {
            Calls++;
            return changes ? method.Body with { MaxStack = method.Body.MaxStack + 1 } : method.Body;
        }
    }

    private sealed class RecordingIntegerChecks : IIntegerCheckEliminator
    {
        public CilMethodBody? Rewritten { get; private set; }
        public CilMethodBody Rewrite(CilMethodBody body) => Rewritten = body;
    }

    private sealed class NoExceptionDiscovery : IImplicitExceptionDiscovery
    {
        public ImmutableArray<ReachabilityExceptionRequirement> Discover(CilInstruction instruction) =>
            ImmutableArray<ReachabilityExceptionRequirement>.Empty;
    }

    private sealed class RecordingInstructionAnalyzer : IReachabilityInstructionAnalyzer
    {
        public bool Called { get; private set; }

        public ReachabilityInstructionAnalysis Analyze(ReachabilityInstructionRequest request)
        {
            Called = true;
            return new([], [], [], [], [], [], [], [], [], []);
        }
    }

    private sealed class RecordingGraphBuilder : IControlFlowGraphBuilder
    {
        public ControlFlowGraph? Graph { get; private set; }
        public int Calls { get; private set; }

        public ControlFlowGraph Build(CilMethodBody methodBody)
        {
            Calls++;
            return Graph = new ControlFlowGraphBuilderFactory().Create().Build(methodBody);
        }
    }

    private sealed class FixedStackValidatorFactory(ITypedStackValidator? validator = null) : ITypedStackValidatorFactory
    {
        public ITypedStackValidator Create(
            ITypeRepository types,
            IFieldRepository fields,
            IMethodRepository methods) => validator ?? new FixedStackValidator();
    }

    private sealed class RejectingStackValidator(CompilerException failure) : ITypedStackValidator
    {
        public ValidatedControlFlowGraph Validate(ControlFlowGraph graph) => throw failure;
    }

    private sealed class FixedStackValidator : ITypedStackValidator
    {
        public ValidatedControlFlowGraph Validate(ControlFlowGraph graph) =>
            new(
                graph,
                graph.Blocks.ToImmutableDictionary(
                    block => block.Index,
                    _ => ImmutableArray<CliValueKind>.Empty),
                graph.Blocks.SelectMany(block => block.Instructions)
                    .ToImmutableDictionary(
                        instruction => instruction.Offset,
                        _ => ImmutableArray<CliValueKind>.Empty));
    }

    private sealed class UnusedTypeRepository : ITypeRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) =>
            throw new InvalidOperationException();
    }

    private sealed class UnusedFieldRepository : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) =>
            throw new InvalidOperationException();
    }

    private sealed class UnusedMethodRepository : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) =>
            throw new InvalidOperationException();
    }
}
