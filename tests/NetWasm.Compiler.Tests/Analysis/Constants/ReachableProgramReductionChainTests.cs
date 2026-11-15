// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

public sealed class ReachableProgramReductionChainTests
{
    [Fact]
    public void RejectsMissingInputsBeforeCallingAnyReducer()
    {
        Assert.Throws<ArgumentNullException>(() => new ReachableProgramReductionChain(null!));
        Assert.Throws<ArgumentException>(() => new ReachableProgramReductionChain([null!]));
        var reducer = new RecordingReducer(ImmutableDictionary<string, CilMethodBody>.Empty);
        var chain = AsReducer(new ReachableProgramReductionChain([reducer]));
        var program = ConstantArgumentProgramReducerTests.Program(ConstantArgumentProgramReducerTests.Managed());

        Assert.Throws<ArgumentNullException>(() => chain.Reduce(null!, ReachabilityRoots.Empty));
        Assert.Throws<ArgumentNullException>(() => chain.Reduce(program, null!));
        Assert.Equal(0, reducer.Calls);
        Assert.Empty(AsReducer(new ReachableProgramReductionChain([])).Reduce(program, ReachabilityRoots.Empty));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StopsAtTheFirstChangeSoTheCallerCanRebuildFacts(bool changes)
    {
        var method = ConstantArgumentProgramReducerTests.Managed();
        var program = ConstantArgumentProgramReducerTests.Program(method);
        var result = changes ? ImmutableDictionary<string, CilMethodBody>.Empty.Add(method.Method.CanonicalName, method.Body)
            : ImmutableDictionary<string, CilMethodBody>.Empty;
        var first = new RecordingReducer(ImmutableDictionary<string, CilMethodBody>.Empty);
        var second = new RecordingReducer(result);
        var third = new RecordingReducer(ImmutableDictionary<string, CilMethodBody>.Empty);
        var chain = AsReducer(new ReachableProgramReductionChain([first, second, third]));

        Assert.Equal(result, chain.Reduce(program, ReachabilityRoots.Empty));
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
        Assert.Equal(changes ? 0 : 1, third.Calls);
    }

    private static IReachableProgramReducer AsReducer(IReachableProgramReducer reducer) => reducer;

    private sealed class RecordingReducer(ImmutableDictionary<string, CilMethodBody> result) : IReachableProgramReducer
    {
        public int Calls { get; private set; }
        public ImmutableDictionary<string, CilMethodBody> Reduce(ReachableProgram program, ReachabilityRoots roots)
        {
            Calls++;
            return result;
        }
    }
}
