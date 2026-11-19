// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A program built against a library that names a member or a type the library it runs with lacks compiles, as .NET
// runs it: each reached method naming such a member throws MissingMethodException or MissingFieldException when
// called, and one naming such a type throws TypeLoadException, whether a type token names it or a member reference's
// declaring type does, as .NET throws when it first compiles the method and Native AOT compiles it to a throw, and the
// build warns NW1016 for each one. A missing instance method's message names the type the reference names, not a
// base the lookup reached. A method nothing reaches costs nothing, and NetWasmMissingApiAsError makes the reference
// an error again.
public sealed class MissingMemberCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void AReachedMethodNamingAMissingMemberWarnsAndThrowsWhenCalled(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var logs = new CapturingLoggerProvider();

        var result = Compile(Options(assets, target), logs);

        var warnings = logs.Entries.Where(entry => entry.Level == LogLevel.Warning).ToArray();
        Assert.All(warnings, warning => Assert.Equal("NW1016", warning.Id.Name));
        Assert.Equal(5, warnings.Length);
        Assert.Single(warnings, warning => warning.Message.Contains("CallGone", StringComparison.Ordinal) &&
            warning.Message.Contains("Gone()", StringComparison.Ordinal) &&
            warning.Message.Contains("MissingMethodException", StringComparison.Ordinal));
        Assert.Single(warnings, warning => warning.Message.Contains("ReadGone", StringComparison.Ordinal) &&
            warning.Message.Contains("GoneField", StringComparison.Ordinal) &&
            warning.Message.Contains("MissingFieldException", StringComparison.Ordinal));
        Assert.Single(warnings, warning => warning.Message.Contains("CallGoneInstance", StringComparison.Ordinal) &&
            warning.Message.Contains("MissingMemberLibrary.Holder.GoneInstance()", StringComparison.Ordinal));
        Assert.Single(warnings, warning => warning.Message.Contains("UseGoneType", StringComparison.Ordinal) &&
            warning.Message.Contains("'MissingMemberLibrary.GoneType'", StringComparison.Ordinal) &&
            warning.Message.Contains("TypeLoadException", StringComparison.Ordinal));
        Assert.Single(warnings, warning => warning.Message.Contains("CallGoneType", StringComparison.Ordinal) &&
            warning.Message.Contains("TypeLoadException", StringComparison.Ordinal));
        Assert.DoesNotContain(warnings, warning => warning.Message.Contains("NeverCalled", StringComparison.Ordinal));
        // Kept (2), then each missing member's or type's exception with what it lacks named.
        Assert.Equal(1_111_102, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 0, target,
            result.StaticDataEnd));
    }

    [Fact]
    public void TheProjectCanMakeAMissingMemberAnError()
    {
        using var assets = TestAssets.Create();

        var error = Assert.Throws<CompilerException>(() =>
            Compile(Options(assets, WasmTarget.Wasm32) with { MissingApiAsError = true }, new CapturingLoggerProvider()));

        Assert.Equal(DiagnosticCode.MissingApi, error.Diagnostic.Code);
        Assert.Matches("CallGone|ReadGone|UseGoneType", error.Diagnostic.Method);
        Assert.Matches("::Gone(Field|Instance)?' did not resolve|GoneType' was not found", error.Diagnostic.Message);
        Assert.Equal(new AssemblyIdentity("MissingMemberApplication"), error.Diagnostic.Context!.Source.Assembly);
    }

    // The program is built against the library's first version, which has Gone and GoneField; it runs with the
    // second, which has neither.
    private static CompilerOptions Options(TestAssets assets, WasmTarget target)
    {
        var library = assets.CompileSource("MissingMemberLibrary", """
            namespace MissingMemberLibrary;
            public static class Api
            {
                public static int GoneField = 3;
                public static int Gone() => 1;
                public static int Kept() => 2;
            }
            public sealed class Holder
            {
                public int GoneInstance() => 7;
            }
            public sealed class GoneType
            {
                public static int Value() => 4;
            }
            """);
        var application = assets.CompileSource("MissingMemberApplication", """
            using System;
            using System.Runtime.CompilerServices;
            namespace MissingMemberApplication;

            public static class EntryPoint
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int CallGone() => MissingMemberLibrary.Api.Gone();

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int ReadGone() => MissingMemberLibrary.Api.GoneField;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int CallGoneInstance() => new MissingMemberLibrary.Holder().GoneInstance();

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int UseGoneType() => typeof(MissingMemberLibrary.GoneType).GetHashCode();

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int CallGoneType() => MissingMemberLibrary.GoneType.Value();

                public static int NeverCalled() => MissingMemberLibrary.Api.Gone() + MissingMemberLibrary.Api.GoneField;

                public static int Run(int input)
                {
                    var result = MissingMemberLibrary.Api.Kept();
                    try { result += CallGone(); }
                    catch (MissingMethodException error) { result += error.Message.Contains("Gone") ? 100 : 50; }
                    try { result += ReadGone(); }
                    catch (MissingFieldException error) { result += error.Message.Contains("GoneField") ? 1000 : 500; }
                    try { result += CallGoneInstance(); }
                    catch (MissingMethodException error)
                    {
                        result += error.Message.Contains("MissingMemberLibrary.Holder.GoneInstance") ? 10_000 : 5_000;
                    }
                    try { result += UseGoneType(); }
                    catch (TypeLoadException error)
                    {
                        result += error.Message.Contains("'MissingMemberLibrary.GoneType'") ? 100_000 : 50_000;
                    }
                    try { result += CallGoneType(); }
                    catch (TypeLoadException error)
                    {
                        result += error.Message.Contains("'MissingMemberLibrary.GoneType'") ? 1_000_000 : 500_000;
                    }
                    return result;
                }
            }
            """, library);
        _ = assets.CompileSource("MissingMemberLibrary", """
            namespace MissingMemberLibrary;
            public static class Api
            {
                public static int Kept() => 2;
            }
            public sealed class Holder
            {
            }
            """);
        return new(application, [assets.CoreLib, library], "MissingMemberApplication.EntryPoint", "Run", [],
            Target: target);
    }

    private static CompilationResult Compile(CompilerOptions options, CapturingLoggerProvider logs)
    {
        options = options with
        {
            ReferencePaths = [.. TestAssets.WithFrameworkReferences(options.EntryAssemblyPath, options.ReferencePaths)],
        };
        using var services = new ServiceCollection()
            .AddNetWasmCompiler()
            .AddSingleton<ILoggerProvider>(logs)
            .BuildServiceProvider();
        return services.GetRequiredService<INetWasmCompiler>().Compile(options);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<(LogLevel Level, EventId Id, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                Entries.Enqueue((logLevel, eventId, formatter(state, exception)));
        }
        public void Dispose() { }
    }
}
