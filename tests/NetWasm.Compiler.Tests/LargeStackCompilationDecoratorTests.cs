// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class LargeStackCompilationDecoratorTests
{
    // A compile runs on a large-stack thread whoever calls it, and returns what the compiler returned.
    [Fact]
    public void ACompileRunsOnALargeStackThread()
    {
        var compiler = new RecordingCompiler();
        var options = Options();

        var result = new LargeStackCompilationDecorator(compiler, Threads).Compile(options);

        Assert.Same(RecordingCompiler.Result, result);
        Assert.Same(options, compiler.Options);
        Assert.True(compiler.OnLargeStack);
        Assert.NotEqual(Environment.CurrentManagedThreadId, compiler.Thread);
    }

    // A caller already on a large-stack thread, as nwc's command is, keeps its thread.
    [Fact]
    public void ACompileOnALargeStackThreadStaysOnIt()
    {
        var compiler = new RecordingCompiler();

        var caller = LargeStackThreads.Call(() =>
        {
            new LargeStackCompilationDecorator(compiler, Threads).Compile(Options());
            return Environment.CurrentManagedThreadId;
        });

        Assert.Equal(caller, compiler.Thread);
    }

    // A host without threads, as a browser is, compiles on the calling thread.
    [Fact]
    public void AHostWithoutThreadsCompilesOnTheCallingThread()
    {
        var compiler = new RecordingCompiler();

        new LargeStackCompilationDecorator(compiler, CompilerParallelism.ForHost(isBrowser: true, 8)).Compile(Options());

        Assert.Equal(Environment.CurrentManagedThreadId, compiler.Thread);
        Assert.False(compiler.OnLargeStack);
    }

    [Fact]
    public void ItRejectsMissingCollaboratorsAndOptions()
    {
        Assert.Throws<ArgumentNullException>(() => new LargeStackCompilationDecorator(null!, Threads));
        Assert.Throws<ArgumentNullException>(() => new LargeStackCompilationDecorator(new RecordingCompiler(), null!));
        Assert.Throws<ArgumentNullException>(() =>
            new LargeStackCompilationDecorator(new RecordingCompiler(), Threads).Compile(null!));
    }

    private static readonly CompilerParallelism Threads = CompilerParallelism.ForHost(isBrowser: false, 8);

    private static CompilerOptions Options() => new("app.dll", [], "Program", "Main", []);

    private sealed class RecordingCompiler : INetWasmCompiler
    {
        public static readonly CompilationResult Result = new([], null!, null!, null!, StaticDataEnd: 0);

        public CompilerOptions? Options { get; private set; }
        public bool OnLargeStack { get; private set; }
        public int Thread { get; private set; }

        public CompilationResult Compile(CompilerOptions options)
        {
            Options = options;
            OnLargeStack = LargeStackThreads.IsCurrentThread;
            Thread = Environment.CurrentManagedThreadId;
            return Result;
        }
    }
}
