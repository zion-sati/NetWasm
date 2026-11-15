// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Analysis.StaticArrays;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.StaticArrays;

using static StaticArrayTestSupport;

public sealed class StaticArrayPopulationReducerTests
{
    [Fact]
    public void RejectsMissingInputsBeforePlanning()
    {
        var observations = new Observations([]);
        var planner = new RecordingPlanner([]);
        Assert.Throws<ArgumentNullException>(() => new StaticArrayPopulationReducer(null!, planner));
        Assert.Throws<ArgumentNullException>(() => new StaticArrayPopulationReducer(observations, null!));
        var reducer = AsReducer(new StaticArrayPopulationReducer(observations, planner));

        Assert.Throws<ArgumentNullException>(() => reducer.Reduce(null!, ReachabilityRoots.Empty));
        Assert.Throws<ArgumentNullException>(() => reducer.Reduce(Program(Initializer()), null!));
        Assert.Null(planner.Method);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RewritesOnlyOffsetsProvenByThePlanner(bool changes)
    {
        var method = Initializer();
        var program = Program(method);
        var observed = ImmutableHashSet.Create(Table.Key);
        var planner = new RecordingPlanner(changes ? [2, 3, 4, 5, 6, 7] : []);
        var reducer = AsReducer(new StaticArrayPopulationReducer(new Observations(observed), planner));

        var result = reducer.Reduce(program, ReachabilityRoots.Empty);

        Assert.Same(method, planner.Method);
        Assert.Same(observed, planner.Observed);
        if (!changes)
        {
            Assert.Empty(result);
            return;
        }
        var rewritten = Assert.Single(result).Value;
        Assert.Same(method.Body.Instructions[0], rewritten.Instructions[0]);
        Assert.Same(method.Body.Instructions[1], rewritten.Instructions[1]);
        Assert.Same(method.Body.Instructions[8], rewritten.Instructions[8]);
        Assert.Same(method.Body.Instructions[9], rewritten.Instructions[9]);
        Assert.All(rewritten.Instructions[2..8], instruction =>
        {
            Assert.Equal(CilOperation.Nop, instruction.Operation);
            Assert.IsType<CilOperand.None>(instruction.Operand);
            Assert.Equal(method.Body.Instructions[instruction.Offset].SourceOffset, instruction.SourceOffset);
        });
    }

    private static IReachableProgramReducer AsReducer(IReachableProgramReducer reducer) => reducer;

    private sealed class Observations(ImmutableHashSet<EntityKey> observed) : IStaticFieldObservationCollector
    {
        public ImmutableHashSet<EntityKey> Collect(ReachableProgram program, ReachabilityRoots roots) => observed;
    }

    private sealed class RecordingPlanner(ImmutableHashSet<int> offsets) : IStaticArrayPopulationPlanner
    {
        public ManagedMethodBody? Method { get; private set; }
        public ImmutableHashSet<EntityKey>? Observed { get; private set; }
        public ImmutableHashSet<int> Plan(ManagedMethodBody method, ImmutableHashSet<EntityKey> observedFields)
        {
            Method = method;
            Observed = observedFields;
            return offsets;
        }
    }
}
