using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

using static ConstantRewriteTestSupport;

public sealed class ConstantArgumentProgramReducerTests
{
    [Fact]
    public void RejectsMissingInputsAndContradictoryIncomingSignatures()
    {
        var body = Managed();
        var calls = new FixedArguments([new(body.Method, []), new(body.Method, [new ScalarConstant.Int32(1)])]);
        var rewrites = new RecordingRewrites();
        Assert.Throws<ArgumentNullException>(() => new ConstantArgumentProgramReducer(null!, rewrites));
        Assert.Throws<ArgumentNullException>(() => new ConstantArgumentProgramReducer(calls, null!));
        var reducer = AsReducer(new ConstantArgumentProgramReducer(calls, rewrites));
        Assert.Throws<ArgumentNullException>(() => reducer.Reduce(null!, ReachabilityRoots.Empty));
        Assert.Throws<ArgumentNullException>(() => reducer.Reduce(Program(body), null!));
        Assert.Throws<InvalidOperationException>(() => reducer.Reduce(Program(body), ReachabilityRoots.Empty));
        Assert.Null(rewrites.Body);
    }

    [Theory]
    [InlineData(CliValueKind.ManagedReference, true)]
    [InlineData(CliValueKind.I4, false)]
    [InlineData(CliValueKind.NativeInt, false)]
    public void RewritesNullOnlyForReferenceParameters(CliValueKind kind, bool changes)
    {
        var original = Managed();
        var signature = MethodSignatureModel.Create(kind, kind);
        var method = original.Method with { Signature = signature, Definition = original.Method.Definition with { Signature = signature } };
        var body = original.Body with { Method = method.Definition, MethodInstance = method };
        var managed = new ManagedMethodBody(method, new(new ControlFlowGraphBuilderFactory().Create().Build(body),
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty));
        var rewrites = new RecordingRewrites();
        var reducer = AsReducer(new ConstantArgumentProgramReducer(new FixedArguments([new(method, [new ScalarConstant.Null()])]), rewrites));

        var result = reducer.Reduce(Program(managed), ReachabilityRoots.Empty);

        if (changes)
            Assert.Equal(CilOperation.LoadNull, Assert.Single(result).Value.Instructions[0].Operation);
        else
        {
            Assert.Empty(result);
            Assert.Null(rewrites.Body);
        }
    }

    [Theory]
    [InlineData(7, true)]
    [InlineData(8, false)]
    [InlineData(null, false)]
    public void RequiresAgreementFromEveryIncomingCall(int? second, bool changes)
    {
        var body = Managed();
        var collector = new FixedArguments([
            new(body.Method, [new ScalarConstant.Int32(7)]),
            new(body.Method, [second is { } value ? new ScalarConstant.Int32(value) : null]),
        ]);
        var rewrites = new RecordingRewrites();
        var reducer = AsReducer(new ConstantArgumentProgramReducer(collector, rewrites));

        var result = reducer.Reduce(Program(body), ReachabilityRoots.Empty);

        if (changes)
        {
            var rewritten = Assert.Single(result).Value;
            Assert.Equal(new CilOperand.ConstantI4(7), rewritten.Instructions[0].Operand);
            Assert.Equal(CilOperation.LoadInt32, rewritten.Instructions[0].Operation);
            Assert.Same(rewritten, rewrites.Body);
        }
        else
        {
            Assert.Empty(result);
            Assert.Null(rewrites.Body);
        }
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
    public void PreservesExternallyCallableBoundaries(int boundary)
    {
        var body = Managed();
        var program = Program(body);
        var roots = ReachabilityRoots.Empty;
        switch (boundary)
        {
            case 0: roots = roots with { Methods = [body.Method.Definition.Key] }; break;
            case 1: program = program with { Exports = [new("entry", body.Method.Definition.Key)] }; break;
            case 2: program = program with { CallableMethods = ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(body.Method.CanonicalName, body.Method) }; break;
            case 3: program = program with { NativeCallbacks = ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(body.Method.CanonicalName, body.Method) }; break;
            case 4: program = program with { MethodDescriptors = ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(body.Method.CanonicalName, body.Method) }; break;
            case 5: program = program with { EntryPoint = body.Method.Definition }; break;
            case 6: roots = roots with { InvokedMethods = [body.Method.Definition.Key] }; break;
            case 7:
                var site = new DispatchCallSiteModel("caller", 0, body.Method, [new(body.Method.DeclaringType, body.Method)]);
                program = program with { DispatchCallSites = ImmutableDictionary<string, DispatchCallSiteModel>.Empty.Add(site.Key, site) };
                break;
        }
        var rewrites = new RecordingRewrites();
        var reducer = AsReducer(new ConstantArgumentProgramReducer(new FixedArguments([new(body.Method, [new ScalarConstant.Int32(7)])]), rewrites));

        Assert.Empty(reducer.Reduce(program, roots));
        Assert.Null(rewrites.Body);
    }

    [Theory]
    [InlineData(CilOperation.LoadArgumentAddress)]
    [InlineData(CilOperation.StoreArgument)]
    public void PreservesAliasedOrMutatedArguments(CilOperation hazard)
    {
        var original = Managed();
        var body = original.Body with
        {
            Instructions = hazard == CilOperation.StoreArgument
                ? [Instruction(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
                    Instruction(1, hazard, new CilOperand.Index(0)),
                    Instruction(2, CilOperation.LoadArgument, new CilOperand.Index(0)), Instruction(3, CilOperation.Return)]
                : [Instruction(0, hazard, new CilOperand.Index(0)), Instruction(1, CilOperation.Pop),
                    Instruction(2, CilOperation.LoadArgument, new CilOperand.Index(0)), Instruction(3, CilOperation.Return)],
        };
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        var method = new ManagedMethodBody(original.Method, new(graph, ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty));
        var reducer = AsReducer(new ConstantArgumentProgramReducer(new FixedArguments([new(method.Method, [new ScalarConstant.Int32(7)])]), new RecordingRewrites()));

        Assert.Empty(reducer.Reduce(Program(method), ReachabilityRoots.Empty));
    }

    private static IReachableProgramReducer AsReducer(IReachableProgramReducer reducer) => reducer;

    internal static ManagedMethodBody Managed()
    {
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4);
        var method = Method();
        method = method with { Definition = method.Definition with { Signature = signature }, Signature = signature };
        var body = new CilMethodBody(method.Definition, 1, [],
            [Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)), Instruction(1, CilOperation.Return)])
        { MethodInstance = method };
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        return new(method, new(graph, ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty));
    }

    internal static ReachableProgram Program(ManagedMethodBody body) => new(null,
        ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty.Add(body.Method.Definition.Key, body),
        [], [], [], [], [], [], ImmutableDictionary<EntityKey, MethodRootMap>.Empty,
        ImmutableDictionary<CliTypeIdentity, MethodInstanceModel>.Empty, []);

    private sealed class FixedArguments(ImmutableArray<ConstantCallEvaluation> calls) : ICallArgumentConstantCollector
    {
        public ImmutableArray<ConstantCallEvaluation> Collect(ManagedMethodBody method) => calls;
    }

    private sealed class RecordingRewrites : IMethodSpecializer
    {
        public CilMethodBody? Body { get; private set; }
        public CilMethodBody Rewrite(CilMethodBody body) => Body = body;
    }
}
