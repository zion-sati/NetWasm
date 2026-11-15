// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

public sealed class ReducingReachabilityClosureBuilderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RebuildsWithRewrittenBodiesUntilThereAreNoChanges(bool suppliedState)
    {
        var body = ConstantArgumentProgramReducerTests.Managed();
        var program = ConstantArgumentProgramReducerTests.Program(body);
        var roots = new ReachabilityRoots([], [], [body.Method.Definition.Key]);
        var rewritten = ImmutableDictionary<string, CilMethodBody>.Empty.Add(body.Method.CanonicalName, body.Body);
        var inner = new RecordingBuilder(program);
        var reducer = new QueueReducer([rewritten, ImmutableDictionary<string, CilMethodBody>.Empty]);
        var builder = AsBuilder(new ReducingReachabilityClosureBuilder(inner, reducer));

        Assert.Same(program, builder.Build(null, [new("entry", body.Method.Definition.Key)],
            suppliedState ? roots : null, suppliedState ? rewritten : null));
        Assert.Equal(2, inner.Inputs.Count);
        if (suppliedState)
            Assert.Same(rewritten, inner.Inputs[0]);
        else
            Assert.Empty(inner.Inputs[0]);
        Assert.Same(body.Body, inner.Inputs[1][body.Method.CanonicalName]);
        Assert.All(inner.Roots, value => Assert.Same(suppliedState ? roots : ReachabilityRoots.Empty, value));
        Assert.Equal(2, reducer.Calls);
    }

    [Fact]
    public void RejectsMissingInputsBeforeBuildingOrReducing()
    {
        var program = ConstantArgumentProgramReducerTests.Program(ConstantArgumentProgramReducerTests.Managed());
        var inner = new RecordingBuilder(program);
        var reducer = new QueueReducer([]);
        Assert.Throws<ArgumentNullException>(() => new ReducingReachabilityClosureBuilder(null!, reducer));
        Assert.Throws<ArgumentNullException>(() => new ReducingReachabilityClosureBuilder(inner, null!));
        var builder = AsBuilder(new ReducingReachabilityClosureBuilder(inner, reducer));

        Assert.Throws<ArgumentNullException>(() => builder.Build(null, null!));
        Assert.Empty(inner.Inputs);
        Assert.Equal(0, reducer.Calls);
    }

    private static IReachabilityClosureBuilder AsBuilder(IReachabilityClosureBuilder builder) => builder;

    private sealed class RecordingBuilder(ReachableProgram program) : IReachabilityClosureBuilder
    {
        public List<ImmutableDictionary<string, CilMethodBody>> Inputs { get; } = [];
        public List<ReachabilityRoots?> Roots { get; } = [];
        public ReachableProgram Build(MethodDefinitionModel? entryPoint, IEnumerable<ProgramExport> exports,
            ReachabilityRoots? roots = null, ImmutableDictionary<string, CilMethodBody>? rewrittenBodies = null)
        {
            Assert.Single(exports);
            Inputs.Add(Assert.IsType<ImmutableDictionary<string, CilMethodBody>>(rewrittenBodies));
            Roots.Add(roots);
            return program;
        }
    }

    private sealed class QueueReducer(IEnumerable<ImmutableDictionary<string, CilMethodBody>> results) : IReachableProgramReducer
    {
        private readonly Queue<ImmutableDictionary<string, CilMethodBody>> _results = new(results);
        public int Calls { get; private set; }
        public ImmutableDictionary<string, CilMethodBody> Reduce(ReachableProgram program, ReachabilityRoots roots)
        {
            Calls++;
            return _results.Dequeue();
        }
    }
}
