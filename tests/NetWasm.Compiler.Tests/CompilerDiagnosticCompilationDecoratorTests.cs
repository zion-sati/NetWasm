// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerDiagnosticCompilationDecoratorTests
{
    [Fact]
    public void CompilePushesTheConfiguredScopeDelegatesAndDisposesIt()
    {
        var result = Result();
        var compiler = new RecordingCompiler(result);
        var logger = new RecordingLogger();
        var decorator = Assert.IsAssignableFrom<INetWasmCompiler>(
            new CompilerDiagnosticCompilationDecorator(
                compiler,
                logger,
                new CompilerDiagnosticLogPathResolver()));
        var options = Options("compiler.jsonl");

        var actual = decorator.Compile(options);

        Assert.Same(result, actual);
        Assert.Same(options, compiler.Options);
        var scope = Assert.IsType<CompilerDiagnosticScope>(Assert.Single(logger.Scopes));
        Assert.Equal("compiler.jsonl", scope.Path);
        Assert.Equal(4000, Assert.Single(logger.Events).Id);
        Assert.True(Assert.Single(logger.Leases).Disposed);
    }

    [Fact]
    public void CompileDisposesTheScopeWhenTheInnerCompilerFails()
    {
        var failure = new InvalidOperationException("failure");
        var compiler = new RecordingCompiler(Result(), failure);
        var logger = new RecordingLogger();
        var decorator = Assert.IsAssignableFrom<INetWasmCompiler>(
            new CompilerDiagnosticCompilationDecorator(
                compiler,
                logger,
                new CompilerDiagnosticLogPathResolver()));

        var actual = Assert.Throws<InvalidOperationException>(() =>
            decorator.Compile(Options(diagnosticLogPath: null)));

        Assert.Same(failure, actual);
        Assert.Same(
            failure,
            Assert.Single(logger.Exceptions, static exception => exception is not null));
        var scope = Assert.IsType<CompilerDiagnosticScope>(Assert.Single(logger.Scopes));
        Assert.Null(scope.Path);
        Assert.Equal([4000, 4001], logger.Events.Select(static entry => entry.Id));
        Assert.True(Assert.Single(logger.Leases).Disposed);
    }

    [Fact]
    public void ConstructorRejectsNullDependencies()
    {
        var compiler = new RecordingCompiler(Result());
        var logger = new RecordingLogger();
        var paths = new CompilerDiagnosticLogPathResolver();

        Assert.Throws<ArgumentNullException>(() =>
            new CompilerDiagnosticCompilationDecorator(null!, logger, paths));
        Assert.Throws<ArgumentNullException>(() =>
            new CompilerDiagnosticCompilationDecorator(compiler, null!, paths));
        Assert.Throws<ArgumentNullException>(() =>
            new CompilerDiagnosticCompilationDecorator(compiler, logger, null!));
    }

    private static CompilerOptions Options(string? diagnosticLogPath)
        => new(
            "application.dll",
            [],
            "Program",
            "Main",
            [],
            DiagnosticLogPath: diagnosticLogPath);

    private static CompilationResult Result()
        => new([], null!, null!, null!, StaticDataEnd: 0);

    private sealed class RecordingCompiler(
        CompilationResult result,
        Exception? failure = null) : INetWasmCompiler
    {
        public CompilerOptions? Options { get; private set; }

        public CompilationResult Compile(CompilerOptions options)
        {
            Options = options;
            if (failure is not null)
            {
                throw failure;
            }

            return result;
        }
    }

    private sealed class RecordingLogger : ILogger<CompilerDiagnosticCompilationDecorator>
    {
        public List<object> Scopes { get; } = [];

        public List<RecordingDisposable> Leases { get; } = [];

        public List<EventId> Events { get; } = [];

        public List<Exception?> Exceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            Scopes.Add(state);
            var lease = new RecordingDisposable();
            Leases.Add(lease);
            return lease;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Events.Add(eventId);
            Exceptions.Add(exception);
        }
    }

    private sealed class RecordingDisposable : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
