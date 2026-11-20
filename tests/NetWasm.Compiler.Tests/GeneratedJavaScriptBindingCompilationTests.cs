// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A library built for net10.0-browser carries the JavaScript bindings .NET's browser binding generator wrote: each
// [JSImport] gets a [DebuggerNonUserCode] body that binds it through JSFunctionBinding, and each [JSExport] stays as
// written beside a [DebuggerNonUserCode] __Wrapper_<method>_<hash> that .NET's browser runtime registers. Where a
// JavaScript host runs the program they bind as NetWasm's own interop, the import bodies set aside; without one the
// import bodies stay, and their binding throws PlatformNotSupportedException, as on .NET's platforms without a browser.
// A binding NetWasm's interop does not take stays ordinary code, with NW1018 where a JavaScript host runs the program.
public sealed class GeneratedJavaScriptBindingCompilationTests
{
    // What .NET 10's generator writes for these declarations, reduced to the binding calls (the rest of each body only
    // marshals through the JSMarshalerArgument the binding fills).
    private const string BrowserLibrarySource = """
        using System;
        using System.Diagnostics;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices.JavaScript;
        using System.Threading.Tasks;

        #nullable enable

        namespace BrowserLibrary
        {
        public static unsafe partial class Imports
        {
            private static JSFunctionBinding? s_add;
            private static JSFunctionBinding? s_delayed;
            private static JSFunctionBinding? s_each;

            [JSImport("add", "lib.math")]
            [DebuggerNonUserCode]
            internal static int Add(int a, int b)
            {
                s_add ??= JSFunctionBinding.BindJSFunction("add", "lib.math",
                    [JSMarshalerType.Int32, JSMarshalerType.Int32, JSMarshalerType.Int32]);
                Span<JSMarshalerArgument> arguments = stackalloc JSMarshalerArgument[4];
                arguments[0].Initialize();
                arguments[1].Initialize();
                arguments[2].ToJS(a);
                arguments[3].ToJS(b);
                JSFunctionBinding.InvokeJS(s_add, arguments);
                arguments[1].ToManaged(out int result);
                return result;
            }

            [JSImport("delayed", "lib.math")]
            [DebuggerNonUserCode]
            internal static Task<int> DelayedAsync(int a)
            {
                s_delayed ??= JSFunctionBinding.BindJSFunction("delayed", "lib.math",
                    [JSMarshalerType.Task(JSMarshalerType.Int32), JSMarshalerType.Int32]);
                Span<JSMarshalerArgument> arguments = stackalloc JSMarshalerArgument[3];
                arguments[0].Initialize();
                arguments[1].Initialize();
                arguments[2].ToJS(a);
                JSFunctionBinding.InvokeJS(s_delayed, arguments);
                return Task.FromResult(0);
            }

            [JSImport("each", "lib.math")]
            [DebuggerNonUserCode]
            internal static void Each(int count, [JSMarshalAs<JSType.Function<JSType.Number>>] Action<int> callback)
            {
                s_each ??= JSFunctionBinding.BindJSFunction("each", "lib.math",
                    [JSMarshalerType.Discard, JSMarshalerType.Int32, JSMarshalerType.Action(JSMarshalerType.Int32)]);
                Span<JSMarshalerArgument> arguments = stackalloc JSMarshalerArgument[4];
                arguments[0].Initialize();
                arguments[1].Initialize();
                arguments[2].ToJS(count);
                JSFunctionBinding.InvokeJS(s_each, arguments);
            }

            public static int CallAdd(int a, int b) => Add(a, b);

            public static Task<int> CallDelayed(int a) => DelayedAsync(a);

            public static int SumEach(int count)
            {
                var sum = 0;
                Each(count, value => sum += value);
                return sum;
            }
        }

        public static unsafe partial class Exports
        {
            [JSExport]
            public static int Twice(int x) => x * 2;

            [JSExport]
            public static Task<int> Ready(int x) => Task.FromResult(x + 1);

            [JSExport]
            public static void Callback([JSMarshalAs<JSType.Function<JSType.Number>>] Action<int> callback) => callback(7);

            [DebuggerNonUserCode]
            internal static void __Wrapper_Twice_1018901745(JSMarshalerArgument* arguments) => arguments[1].ToJS(Twice(2));

            [DebuggerNonUserCode]
            internal static void __Wrapper_Ready_772531826(JSMarshalerArgument* arguments) { }

            [DebuggerNonUserCode]
            internal static void __Wrapper_Callback_1536757630(JSMarshalerArgument* arguments) { }
        }
        }
        """;

    private const string InitializerSource = """
        namespace System.Runtime.InteropServices.JavaScript
        {
            [global::System.Runtime.CompilerServices.CompilerGenerated]
            unsafe class __GeneratedInitializer
            {
                [global::System.ThreadStatic]
                static bool initialized;

                [global::System.Runtime.CompilerServices.ModuleInitializer]
                internal static void __TrimmingPreserve_()
                {
                }

                static void __Register_()
                {
                    if (initialized || RuntimeInformation.OSArchitecture != Architecture.Wasm)
                        return;
                    initialized = true;
                    JSFunctionBinding.BindManagedFunction("[BrowserLibrary]BrowserLibrary.Exports:Twice", 1018901745,
                        [JSMarshalerType.Int32, JSMarshalerType.Int32]);
                }
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void GeneratedImportsAreHostImportsWhereAJavaScriptHostRunsTheProgram(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileUnsafeSource("BrowserLibrary", BrowserLibrarySource + InitializerSource);
        var assembly = assets.CompileSource("BrowserApplication", """
            public static class EntryPoint
            {
                public static int Run(int input) =>
                    BrowserLibrary.Imports.CallAdd(input, 1) + BrowserLibrary.Imports.SumEach(input) +
                    BrowserLibrary.Imports.CallDelayed(input).Id;
            }
            """, library);

        var (result, warnings) = Compile(new CompilerOptions(assembly, [assets.CoreLib, library], "EntryPoint", "Run", [],
            Target: target));

        var imports = result.InteropManifest.Imports.Select(import => (import.Module, import.Name)).ToHashSet();
        Assert.Contains(("lib.math", "add"), imports);
        Assert.Contains(("lib.math", "delayed"), imports);
        Assert.Contains(("lib.math", "each"), imports);
        Assert.Contains(result.InteropManifest.Callbacks, callback => callback.ImportName == "each");
        // The generator's bodies are set aside, so nothing reaches its binding surface.
        Assert.DoesNotContain(result.Program.Methods.Values, body =>
            body.Method.DeclaringType.FullName == "System.Runtime.InteropServices.JavaScript.JSFunctionBinding");
        // Only the export NetWasm cannot take warns; every import binds.
        var warning = Assert.Single(warnings);
        Assert.Contains("BrowserLibrary.Exports::Callback", warning.Message, StringComparison.Ordinal);
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void GeneratedExportsAreExportedAndOnesNetWasmCannotTakeStayOrdinaryWithAWarning(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileUnsafeSource("BrowserLibrary", BrowserLibrarySource + InitializerSource);
        var assembly = assets.CompileSource("BrowserApplication", """
            public static class EntryPoint
            {
                public static int Run(int input) => input;
            }
            """, library);
        var options = CompilerTestSupport.CreateReactorOptions(assets, assembly, "EntryPoint", "Run", target);

        var (result, warnings) = Compile(options with
        {
            ReferencePaths = [.. options.ReferencePaths, library],
            UseJavaScriptExportBoundary = true,
        });

        var exports = result.InteropManifest.Exports.Select(export => export.Name).ToArray();
        Assert.Contains("Twice", exports);
        Assert.Contains("Ready", exports);
        Assert.DoesNotContain("Callback", exports);
        // Nothing registers the generator's wrappers, as on .NET without a browser.
        Assert.DoesNotContain(result.Program.Methods.Values, body => body.Method.Definition.Name.StartsWith(
            "__Wrapper_", StringComparison.Ordinal));
        var warning = Assert.Single(warnings);
        Assert.Equal("NW1018", warning.Id.Name);
        Assert.Contains("BrowserLibrary.Exports::Callback", warning.Message, StringComparison.Ordinal);
        Assert.Contains("JavaScript cannot call it", warning.Message, StringComparison.Ordinal);
    }

    // Without a JavaScript host nothing is exported, so a Task-returning [JSExport] a program calls from C# is an
    // ordinary method, whether .NET's generator or NetWasm's own source declared it; it used to stop the build with
    // NW1008, the async export boundary asking for host imports such a program has none of.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void AJSExportReturningATaskIsAnOrdinaryCallWithoutAJavaScriptHost(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileUnsafeSource("BrowserLibrary", BrowserLibrarySource + InitializerSource);
        var assembly = assets.CompileSource("BrowserApplication", """
            using System.Runtime.InteropServices.JavaScript;
            using System.Threading.Tasks;

            public static class EntryPoint
            {
                [JSExport]
                public static Task<int> Own(int x) => Task.FromResult(x * 3);

                public static int Run(int input) =>
                    BrowserLibrary.Exports.Ready(input).Result + Own(input).Result + BrowserLibrary.Exports.Twice(input);
            }
            """, library);
        var options = CompilerTestSupport.CreateReactorOptions(assets, assembly, "EntryPoint", "Run", target);

        var (result, warnings) = Compile(options with { ReferencePaths = [.. options.ReferencePaths, library] });

        Assert.Empty(result.InteropManifest.Exports);
        Assert.Empty(warnings);
        // 41 + 1, 41 * 3 and 41 * 2.
        Assert.Equal(247, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 41,
            target, result.StaticDataEnd));
    }

    // NetWasm's own export, which no generator wrapped, still has to take a shape NetWasm's interop takes.
    [Fact]
    public void AnExportOfNetWasmsOwnWithADelegateParameterStillStopsTheBuild()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("OwnExports", """
            using System;
            using System.Runtime.InteropServices.JavaScript;

            public static class EntryPoint
            {
                [JSExport]
                public static void Callback([JSMarshalAs<JSType.Function<JSType.Number>>] Action<int> callback) => callback(7);

                public static int Run(int input) => input;
            }
            """);
        var options = CompilerTestSupport.CreateReactorOptions(assets, assembly, "EntryPoint", "Run");

        var error = Assert.Throws<CompilerException>(() => Compile(options with { UseJavaScriptExportBoundary = true }));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, error.Diagnostic.Code);
        Assert.Contains("JSExport currently supports", error.Diagnostic.Message, StringComparison.Ordinal);
    }

    private static (CompilationResult Result, (LogLevel Level, EventId Id, string Message)[] Warnings) Compile(
        CompilerOptions options)
    {
        var logs = new CapturingLoggerProvider();
        using var services = new ServiceCollection()
            .AddNetWasmCompiler()
            .AddSingleton<ILoggerProvider>(logs)
            .BuildServiceProvider();
        var result = services.GetRequiredService<INetWasmCompiler>().Compile(options);
        return (result, [.. logs.Entries.Where(entry => entry.Level == LogLevel.Warning)]);
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
