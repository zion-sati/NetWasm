using System;
using System.Threading.Tasks;

using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerDiagnosticScopePusherTests
{
    [Fact]
    public void PushRestoresNestedScopesInReverseOrder()
    {
        var state = new CompilerDiagnosticScopeState();
        var pusher = Assert.IsAssignableFrom<ICompilerDiagnosticScopePusher>(
            new CompilerDiagnosticScopePusher(state));
        var reader = Assert.IsAssignableFrom<ICompilerDiagnosticScopeReader>(
            new CompilerDiagnosticScopeReader(state));
        var outer = new CompilerDiagnosticScope("outer.jsonl");
        var inner = new CompilerDiagnosticScope("inner.jsonl");

        var outerLease = pusher.Push(outer);
        Assert.Same(outer, reader.Read());
        var innerLease = pusher.Push(inner);
        Assert.Same(inner, reader.Read());

        innerLease.Dispose();
        Assert.Same(outer, reader.Read());
        outerLease.Dispose();
        outerLease.Dispose();
        Assert.Null(reader.Read());
    }

    [Fact]
    public void DisposeRejectsOutOfOrderScopesWithoutLosingEitherScope()
    {
        var state = new CompilerDiagnosticScopeState();
        var pusher = Assert.IsAssignableFrom<ICompilerDiagnosticScopePusher>(
            new CompilerDiagnosticScopePusher(state));
        var reader = Assert.IsAssignableFrom<ICompilerDiagnosticScopeReader>(
            new CompilerDiagnosticScopeReader(state));
        var outer = new CompilerDiagnosticScope("outer.jsonl");
        var inner = new CompilerDiagnosticScope("inner.jsonl");
        var outerLease = pusher.Push(outer);
        var innerLease = pusher.Push(inner);

        Assert.Throws<InvalidOperationException>(outerLease.Dispose);
        Assert.Same(inner, reader.Read());

        innerLease.Dispose();
        Assert.Same(outer, reader.Read());
        outerLease.Dispose();
        Assert.Null(reader.Read());
    }

    [Fact]
    public async Task PushFlowsAndIsolatesScopesAcrossAsyncExecution()
    {
        var state = new CompilerDiagnosticScopeState();
        var pusher = Assert.IsAssignableFrom<ICompilerDiagnosticScopePusher>(
            new CompilerDiagnosticScopePusher(state));
        var reader = Assert.IsAssignableFrom<ICompilerDiagnosticScopeReader>(
            new CompilerDiagnosticScopeReader(state));
        var outer = new CompilerDiagnosticScope("outer.jsonl");
        var inner = new CompilerDiagnosticScope("inner.jsonl");
        using var outerLease = pusher.Push(outer);

        await Task.Run(() =>
        {
            Assert.Same(outer, reader.Read());
            using var innerLease = pusher.Push(inner);
            Assert.Same(inner, reader.Read());
        }, TestContext.Current.CancellationToken);

        Assert.Same(outer, reader.Read());
    }
}
