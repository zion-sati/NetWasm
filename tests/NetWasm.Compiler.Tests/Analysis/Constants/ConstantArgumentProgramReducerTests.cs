// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
        Assert.Throws<ArgumentNullException>(() => new ConstantArgumentProgramReducer(null!, rewrites, new GraphAnalyzer()));
        Assert.Throws<ArgumentNullException>(() => new ConstantArgumentProgramReducer(calls, null!, new GraphAnalyzer()));
        Assert.Throws<ArgumentNullException>(() => new ConstantArgumentProgramReducer(calls, rewrites, null!));
        var reducer = AsReducer(new ConstantArgumentProgramReducer(calls, rewrites, new GraphAnalyzer()));
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
        var reducer = AsReducer(new ConstantArgumentProgramReducer(new FixedArguments([new(method, [new ScalarConstant.Null()])]), rewrites, new GraphAnalyzer()));

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
        var reducer = AsReducer(new ConstantArgumentProgramReducer(collector, rewrites, new GraphAnalyzer()));

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
        var reducer = AsReducer(new ConstantArgumentProgramReducer(new FixedArguments([new(body.Method, [new ScalarConstant.Int32(7)])]), rewrites, new GraphAnalyzer()));

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
        var reducer = AsReducer(new ConstantArgumentProgramReducer(new FixedArguments([new(method.Method, [new ScalarConstant.Int32(7)])]), new RecordingRewrites(), new GraphAnalyzer()));

        Assert.Empty(reducer.Reduce(Program(method), ReachabilityRoots.Empty));
    }

    // A rewrite that makes a caller pass a constant specializes the callee in the same reduction: the caller's rewritten
    // body is analyzed again and its calls collected again, without another closure.
    [Fact]
    public void ACalleeTheRewriteMakesConstantChangesInTheSameReduction()
    {
        var outer = Managed(1);
        var caller = Managed(2);
        var callee = Managed(3);
        Assert.NotEqual(caller.Method.CanonicalName, callee.Method.CanonicalName);
        // The outer method passes 7 to the caller, which passes its own argument to the callee: unknown until the
        // caller's load of it becomes the constant.
        var collector = new BodyArguments(body =>
            body.Method == outer.Method ? [new(caller.Method, [new ScalarConstant.Int32(7)])]
            : body.Method == caller.Method
                ? [new(callee.Method, [body.Body.Instructions[0].Operation == CilOperation.LoadInt32 ? new ScalarConstant.Int32(7) : null])]
                : []);
        var analyzer = new GraphAnalyzer();
        var reducer = AsReducer(new ConstantArgumentProgramReducer(collector, new RecordingRewrites(), analyzer));
        var program = Program(outer) with
        {
            Methods = Program(outer).Methods.Add(caller.Method.Definition.Key, caller).Add(callee.Method.Definition.Key, callee),
        };

        var result = reducer.Reduce(program, ReachabilityRoots.Empty);

        Assert.Equal([caller.Method.CanonicalName, callee.Method.CanonicalName], result.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(CilOperation.LoadInt32, result[callee.Method.CanonicalName].Instructions[0].Operation);
        Assert.Equal(2, analyzer.Analyzed);
    }

    // A switch on a parameter (as the reader lowers it, a test per case) whose callers pass only some of the case values
    // never takes the others' tests, so the cases only they reach (ThrowHelper.GetArgumentName's names a program never
    // passes) become unreachable: such a test's ldc.i4 becomes a pop and its bne.un a br to the next test.
    [Theory]
    [InlineData(new[] { 0, 2 }, new[] { 1 })]
    [InlineData(new[] { 1, 7 }, new[] { 0, 2 })]
    [InlineData(new[] { 0, 1, 2 }, new int[0])]
    public void ASwitchKeepsOnlyTheTestsOfValuesItsCallersPass(int[] passed, int[] skipped)
    {
        var method = SwitchMethod(3);
        var calls = passed.Select(value => new ConstantCallEvaluation(method.Method, [new ScalarConstant.Int32(value)]))
            .ToImmutableArray();
        var reducer = AsReducer(new ConstantArgumentProgramReducer(new FixedArguments(calls), new RecordingRewrites(), new GraphAnalyzer()));

        var result = reducer.Reduce(Program(method), ReachabilityRoots.Empty);

        if (skipped.Length == 0)
        {
            Assert.Empty(result);
            return;
        }
        Assert.Equal(skipped, Skipped(Assert.Single(result).Value));
    }

    // A caller that passes on its own parameter passes what its callers pass for it; a caller called other than directly
    // (here a delegate's target) may receive anything, so nothing is pruned for it, and past MaximumValues neither. A
    // test that a branch from elsewhere also enters may see another value, so the chain stops before it.
    [Theory]
    [InlineData(false, 2, false, new[] { 0 })]
    [InlineData(true, 2, false, null)]
    [InlineData(false, ConstantArgumentProgramReducer.MaximumValues + 1, false, null)]
    [InlineData(false, 2, true, null)]
    public void AForwardedParameterCarriesItsCallersValues(bool openCaller, int values, bool enteredFromElsewhere, int[]? skipped)
    {
        var outer = Managed(1);
        var caller = Managed(2);
        var callee = SwitchMethod(3, enteredFromElsewhere);
        var collector = new BodyArguments(body =>
            body.Method == outer.Method
                ? [.. Enumerable.Range(1, values).Select(value =>
                    new ConstantCallEvaluation(caller.Method, [new ScalarConstant.Int32(value)]))]
                : body.Method == caller.Method
                    ? [new(callee.Method, [null]) { ForwardedParameters = [0] }]
                    : []);
        var program = Program(outer) with
        {
            Methods = Program(outer).Methods.Add(caller.Method.Definition.Key, caller).Add(callee.Method.Definition.Key, callee),
        };
        if (openCaller)
        {
            program = program with
            {
                CallableMethods = ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(caller.Method.CanonicalName, caller.Method),
            };
        }
        var reducer = AsReducer(new ConstantArgumentProgramReducer(collector, new RecordingRewrites(), new GraphAnalyzer()));

        var result = reducer.Reduce(program, ReachabilityRoots.Empty);

        if (skipped is null)
        {
            Assert.False(result.ContainsKey(callee.Method.CanonicalName));
            return;
        }
        Assert.Equal(skipped, Skipped(result[callee.Method.CanonicalName]));
    }

    // M(int p) => p switch { 0 => 10, 1 => 11, 2 => 12, _ => -1 }, as the reader lowers it: 0 ldarg.0; then a test per
    // case at 1, 6 and 11 (dup; ldc.i4 i; bne.un next; pop; br case); 16 pop; 17 the default; the cases at 19, 21, 23.
    // Entered from elsewhere, a branch at the end also goes to the first test.
    private static ManagedMethodBody SwitchMethod(int token, bool enteredFromElsewhere = false)
    {
        var original = Managed(token);
        var instructions = new List<CilInstruction> { Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)) };
        foreach (var (value, target) in new[] { (0, 19), (1, 21), (2, 23) })
        {
            var at = instructions.Count;
            instructions.Add(Instruction(at, CilOperation.Duplicate));
            instructions.Add(Instruction(at + 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(value)));
            instructions.Add(Instruction(at + 2, CilOperation.BranchIfNotEqual, new CilOperand.BranchTarget(at + 5)));
            instructions.Add(Instruction(at + 3, CilOperation.Pop));
            instructions.Add(Instruction(at + 4, CilOperation.Branch, new CilOperand.BranchTarget(target)));
        }
        instructions.Add(Instruction(16, CilOperation.Pop));
        instructions.Add(Instruction(17, CilOperation.LoadInt32, new CilOperand.ConstantI4(-1)));
        instructions.Add(Instruction(18, CilOperation.Return));
        foreach (var (at, value) in new[] { (19, 10), (21, 11), (23, 12) })
        {
            instructions.Add(Instruction(at, CilOperation.LoadInt32, new CilOperand.ConstantI4(value)));
            instructions.Add(Instruction(at + 1, CilOperation.Return));
        }
        if (enteredFromElsewhere)
        {
            instructions.Add(Instruction(25, CilOperation.LoadArgument, new CilOperand.Index(0)));
            instructions.Add(Instruction(26, CilOperation.Branch, new CilOperand.BranchTarget(1)));
        }
        var body = original.Body with { MaxStack = 3, Instructions = [.. instructions] };
        return new(original.Method, new(new ControlFlowGraphBuilderFactory().Create().Build(body),
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty, ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty));
    }

    private static readonly int[] CaseValues = [0, 1, 2];

    // The case values whose tests a rewrite skips: their ldc.i4 became a pop.
    private static int[] Skipped(CilMethodBody body) =>
        [.. CaseValues.Where(value => body.Instructions[2 + value * 5].Operation == CilOperation.Pop &&
            body.Instructions[3 + value * 5].Operation == CilOperation.Branch)];

    private static IReachableProgramReducer AsReducer(IReachableProgramReducer reducer) => reducer;

    internal static ManagedMethodBody Managed(int token = 1)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4);
        var method = Method(token);
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

    private sealed class BodyArguments(Func<ManagedMethodBody, ImmutableArray<ConstantCallEvaluation>> calls) : ICallArgumentConstantCollector
    {
        public ImmutableArray<ConstantCallEvaluation> Collect(ManagedMethodBody method) => calls(method);
    }

    // Analyses a rewritten body as far as the reducer reads it: its control flow.
    private sealed class GraphAnalyzer : IReachableMethodAnalyzer
    {
        public int Analyzed { get; private set; }

        public ReachableMethodAnalysis Analyze(ReachableMethodRequest request)
        {
            Analyzed++;
            var graph = new ControlFlowGraphBuilderFactory().Create().Build(request.RewrittenBody!);
            var body = new ManagedMethodBody(request.Method, new(graph, ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
                ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty));
            return new(request.Method, body, [], [], new([], [], [], [], [], [], [], [], [], [], []))
            {
                ReachableRegions = [],
            };
        }
    }

    private sealed class RecordingRewrites : IMethodSpecializer
    {
        public CilMethodBody? Body { get; private set; }
        public CilMethodBody Rewrite(CilMethodBody body) => Body = body;
    }
}
