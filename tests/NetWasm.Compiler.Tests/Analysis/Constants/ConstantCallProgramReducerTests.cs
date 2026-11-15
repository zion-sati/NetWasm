// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

using static ConstantArgumentProgramReducerTests;

public sealed class ConstantCallProgramReducerTests
{
    [Fact]
    public void RejectsMissingInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new ConstantCallProgramReducer(null!));
        var reducer = AsReducer(new ConstantCallProgramReducer(new Folding(changes: false)));
        Assert.Throws<ArgumentNullException>(() => reducer.Reduce(null!, ReachabilityRoots.Empty));
        Assert.Throws<ArgumentNullException>(() => reducer.Reduce(Program(Managed()), null!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReturnsOnlyBodiesWhoseCallsFolded(bool changes)
    {
        var body = Managed();
        var program = Program(body) with
        {
            // The same method reached as constructed is reduced once.
            ConstructedMethods = ImmutableDictionary<string, ManagedMethodBody>.Empty.Add(body.Method.CanonicalName, body),
        };

        var result = AsReducer(new ConstantCallProgramReducer(new Folding(changes))).Reduce(program, ReachabilityRoots.Empty);

        if (changes)
        {
            Assert.Equal(CilOperation.Nop, Assert.Single(result, entry => entry.Key == body.Method.CanonicalName).Value.Instructions[0].Operation);
        }
        else
        {
            Assert.Empty(result);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FoldingRuleCleansUpOnlyAfterACallFolded(bool changes)
    {
        var body = Managed().Body;
        var cleanup = new RecordingCleanup();
        Assert.Throws<ArgumentNullException>(() => new ConstantCallFoldingRule(null!, cleanup));
        Assert.Throws<ArgumentNullException>(() => new ConstantCallFoldingRule(new Folding(changes), null!));
        var rule = AsRule(new ConstantCallFoldingRule(new Folding(changes), cleanup));
        Assert.Throws<ArgumentNullException>(() => rule.Rewrite(null!));

        var result = rule.Rewrite(body);

        if (changes)
        {
            Assert.Same(cleanup.Body, result);
            Assert.Equal(CilOperation.Nop, result.Instructions[0].Operation);
        }
        else
        {
            Assert.Same(body, result);
            Assert.Null(cleanup.Body);
        }
    }

    private static IReachableProgramReducer AsReducer(IReachableProgramReducer reducer) => reducer;

    private static IMethodRewriteRule AsRule(IMethodRewriteRule rule) => rule;

    private sealed class Folding(bool changes) : IMethodRewriteRule
    {
        public CilMethodBody Rewrite(CilMethodBody body) => changes
            ? body with { Instructions = [body.Instructions[0] with { Operation = CilOperation.Nop }, .. body.Instructions[1..]] }
            : body;
    }

    private sealed class RecordingCleanup : IMethodSpecializer
    {
        public CilMethodBody? Body { get; private set; }
        public CilMethodBody Rewrite(CilMethodBody body) => Body = body;
    }
}
