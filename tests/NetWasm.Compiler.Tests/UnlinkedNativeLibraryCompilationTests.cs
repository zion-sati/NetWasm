using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A P/Invoke into a library the application does not link compiles to a body that throws DllNotFoundException when
// called, as Mono's WebAssembly build fails calls into modules it does not link; the build warns once for each one
// the program reaches. Imports of linked libraries stay imports.
public sealed class UnlinkedNativeLibraryCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CallsIntoAnUnlinkedLibraryThrowDllNotFoundException(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource("UnlinkedNativeFixture", Source);
        var logs = new CapturingLoggerProvider();

        var result = Compile(Options(assets, assembly, target) with { LinkedNativeLibraries = ["libsample"] }, logs);

        var imports = WasmModuleInspection.ReadFunctionImports(result.ApplicationModule);
        Assert.Contains(imports, import => import.Name == "sample_value");
        Assert.DoesNotContain(imports, import => import.Name == "missing_value" || import.Name == "missing_unused");
        var warning = Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal("NW1017", warning.Id.Name);
        Assert.Contains("'missing_value'", warning.Message, StringComparison.Ordinal);
        Assert.Contains("'libmissing'", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnlinkedCallThrowsWhenItRuns()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource("UnlinkedNativeFixture", Source);

        var result = Compile(Options(assets, assembly, WasmTarget.Wasm32, entry: "RunUnlinked") with
        {
            LinkedNativeLibraries = [],
        }, new CapturingLoggerProvider());

        Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 41,
            WasmTarget.Wasm32, result.StaticDataEnd));
    }

    [Fact]
    public void WithoutALinkedLibraryListEveryImportBinds()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource("UnlinkedNativeFixture", Source);
        var logs = new CapturingLoggerProvider();

        var result = Compile(Options(assets, assembly, WasmTarget.Wasm32), logs);

        var imports = WasmModuleInspection.ReadFunctionImports(result.ApplicationModule);
        Assert.Contains(imports, import => import.Name == "missing_value");
        Assert.DoesNotContain(logs.Entries, entry => entry.Level == LogLevel.Warning);
    }

    private static CompilerOptions Options(TestAssets assets, string assembly, WasmTarget target,
        string entry = "Run") =>
        new(assembly, [assets.CoreLib], "UnlinkedNativeFixture.EntryPoint", entry, [], Target: target);

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

    private const string Source = """
        using System;
        using System.Runtime.InteropServices;
        namespace UnlinkedNativeFixture;

        public static class EntryPoint
        {
            [DllImport("libsample")]
            private static extern int sample_value(int input);

            [DllImport("libmissing")]
            private static extern int missing_value(int input);

            [DllImport("libmissing")]
            private static extern int missing_unused(int input);

            public static int Run(int input) =>
                input == 12345 ? sample_value(input) + missing_value(input) : input + 1;

            public static int RunUnlinked(int input)
            {
                try
                {
                    return input == 12345 ? missing_unused(input) : missing_value(input);
                }
                catch (DllNotFoundException exception) when (exception.Message.Contains("libmissing"))
                {
                    return input + 1;
                }
            }
        }
        """;
}
