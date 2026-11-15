// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Pipeline;

namespace NetWasm.Compiler.Tests;

// A compile whose functions fit runs once. One that names methods over a function limit runs again with nothing
// inlined into them, adding the methods each failure names, until it fits or names none not yet excluded; any
// other failure, and a limit naming no method, is reported at once.
public sealed class FunctionLimitRecompilingExecutorTests
{
    private static readonly EntityKey First = new(new AssemblyIdentity("App"), 0x06000001);
    private static readonly EntityKey Second = new(new AssemblyIdentity("App"), 0x06000002);
    private static readonly CompilerOptions Options = new("App.dll", [], "Program", "Main", []);
    private static readonly CompilationOutcome Outcome = new(null, null!);

    [Fact]
    public void ACompileThatFitsRunsOnce()
    {
        var executor = new ScriptedExecutor(Outcome);

        Assert.Same(Outcome, new FunctionLimitRecompilingExecutor(executor).Execute(Options, serveFromLastCompile: true));
        var call = Assert.Single(executor.Calls);
        Assert.Same(Options, call.Options);
        Assert.True(call.ServeFromLastCompile);
    }

    [Fact]
    public void EachNamedMethodIsExcludedUntilTheCompileFits()
    {
        var executor = new ScriptedExecutor(Limit(First), Limit(First, Second), Outcome);

        Assert.Same(Outcome, new FunctionLimitRecompilingExecutor(executor).Execute(Options));
        int[][] excluded = [[], [First.MetadataToken], [First.MetadataToken, Second.MetadataToken]];
        Assert.Equal(excluded, [.. executor.Calls.Select(static call =>
            call.Options.InliningExcludedMethods.Select(static key => key.MetadataToken).Order().ToArray())]);
        Assert.All(executor.Calls, static call => Assert.False(call.ServeFromLastCompile));
        Assert.All(executor.Calls, call => Assert.Equal(Options with { InliningExcludedMethods = call.Options.InliningExcludedMethods },
            call.Options));
    }

    // Excluding the methods again cannot shrink them further.
    [Fact]
    public void ALimitNamingOnlyExcludedMethodsIsReported()
    {
        var repeated = Limit(First);
        var executor = new ScriptedExecutor(Limit(First), repeated);

        Assert.Same(repeated, Assert.Throws<CompilerException>(() =>
            new FunctionLimitRecompilingExecutor(executor).Execute(Options)));
        Assert.Equal(2, executor.Calls.Count);
    }

    [Fact]
    public void ALimitNamingNoMethodAndOtherFailuresAreReportedAtOnce()
    {
        foreach (var failure in new Exception[]
                 {
                     Limit(),
                     new CompilerException(new CompilerDiagnostic(DiagnosticCode.CompilerInvariant, "broken")
                     {
                         LimitExceedingMethods = [First],
                     }),
                     new InvalidOperationException("other"),
                 })
        {
            var executor = new ScriptedExecutor(failure);

            Assert.Same(failure, Assert.ThrowsAny<Exception>(() => new FunctionLimitRecompilingExecutor(executor).Execute(Options)));
            Assert.Single(executor.Calls);
        }
    }

    [Fact]
    public void ItRejectsMissingInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new FunctionLimitRecompilingExecutor(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new FunctionLimitRecompilingExecutor(new ScriptedExecutor(Outcome)).Execute(null!));
    }

    private static CompilerException Limit(params EntityKey[] methods) =>
        new(new CompilerDiagnostic(DiagnosticCode.FunctionLimit, "over", "App::Big")
        {
            LimitExceedingMethods = [.. methods],
        });

    // Returns or throws its script's entries in turn.
    private sealed class ScriptedExecutor(params object[] script) : ICompilationPipelineExecutor
    {
        private int _next;

        public List<(CompilerOptions Options, bool ServeFromLastCompile)> Calls { get; } = [];

        public CompilationOutcome Execute(CompilerOptions options, bool serveFromLastCompile = false)
        {
            Calls.Add((options, serveFromLastCompile));
            return script[_next++] switch
            {
                CompilationOutcome outcome => outcome,
                Exception failure => throw failure,
                var entry => throw new InvalidOperationException($"Unexpected script entry {entry}."),
            };
        }
    }
}
