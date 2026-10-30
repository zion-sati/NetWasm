using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A command world has no host-driven reactor. As on Mono's single-threaded WASI runtime, a
// synchronous program awaits completed work without the reactor interfaces and nothing pumps its
// deferred work, while an asynchronous Main runs its own wasi:io/poll event loop to completion.
public sealed class CommandWorldAwaitCompilationTests
{
    private const string ReactorHost = "netwasm:runtime/reactor-host@1.0.0";

    public static TheoryData<bool, WasmTarget> Configurations => new()
    {
        { false, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void CommandWorldExportAwaitsWithoutTheReactor(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = Compile(assets, $"SyncAwaitExport_{target}_{optimize}", RunSource, optimize);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "SyncAwaitFixture.EntryPoint",
            "Run",
            [new("run", "SyncAwaitFixture.EntryPoint", "Run")],
            target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));

        Assert.DoesNotContain(result.Program.WitImportMethods, method =>
            method.WitImport!.InterfaceName == ReactorHost);
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void SynchronousMainAwaitsWithoutTheReactor(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = Compile(assets, $"SyncAwaitMain_{target}_{optimize}", MainSource, optimize);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "SyncAwaitFixture.Program",
            "Main",
            [],
            target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0",
            EntryPointKind: CompilerEntryPointKind.ManagedExecutable));

        Assert.DoesNotContain(result.Program.WitImportMethods, method =>
            method.WitImport!.InterfaceName == ReactorHost);
        Assert.DoesNotContain("wake", WasmModuleInspection.ReadExportNames(result.ApplicationModule));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(0, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            0,
            target,
            result.StaticDataEnd,
            entryExportName: "run"));
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void AsynchronousMainRunsItsEventLoopToCompletion(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var result = CompileAsynchronousMain(assets, $"AsyncMainLoop_{target}_{optimize}", AsyncMainSource, optimize, target);

        Assert.DoesNotContain(result.Program.WitImportMethods, method =>
            method.WitImport!.InterfaceName == ReactorHost);
        Assert.Contains(result.Program.WitImportMethods, method =>
            method.WitImport is { InterfaceName: "wasi:io/poll@0.2.11", FunctionName: "poll" });
        Assert.DoesNotContain("wake", WasmModuleInspection.ReadExportNames(result.ApplicationModule));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            0,
            target,
            result.StaticDataEnd,
            entryExportName: "run"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AsynchronousMainThatCannotCompleteFailsInsteadOfWaiting(bool optimize)
    {
        using var assets = TestAssets.Create();
        var stalled = CompileAsynchronousMain(
            assets, $"AsyncMainStall_{optimize}", StalledMainSource, optimize, WasmTarget.Wasm32);
        var completed = CompileAsynchronousMain(
            assets, $"AsyncMainCompleted_{optimize}", StalledMainSource.Replace("new TaskCompletionSource<int>().Task",
                "Task.FromResult(7)", StringComparison.Ordinal), optimize, WasmTarget.Wasm32);

        Assert.Equal(0, CompilerTestSupport.ExecuteWithStandardWasiNode(
            completed.ApplicationModule, assets.Directory, 0, WasmTarget.Wasm32, completed.StaticDataEnd,
            entryExportName: "run"));
        var failure = Assert.ThrowsAny<Exception>(() => CompilerTestSupport.ExecuteWithStandardWasiNode(
            stalled.ApplicationModule, assets.Directory, 0, WasmTarget.Wasm32, stalled.StaticDataEnd,
            entryExportName: "run"));
        Assert.Contains("\"kind\":\"exception\"", failure.Message);
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void ProgramWithoutAWorldAwaitsWithoutWitImports(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = Compile(assets, $"SyncAwaitNoWorld_{target}_{optimize}", RunSource, optimize);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "SyncAwaitFixture.EntryPoint",
            "Run",
            [],
            target));

        Assert.Empty(result.Program.WitImportMethods);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReactorWorldKeepsTheHostDrivenReactor(bool optimize)
    {
        using var assets = TestAssets.Create();
        var assembly = Compile(assets, $"SyncAwaitReactor_{optimize}", RunSource, optimize);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "SyncAwaitFixture.EntryPoint",
            "Run",
            [new("run", "SyncAwaitFixture.EntryPoint", "Run")],
            WasmTarget.Wasm32,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/async-platform@1.0.0"));

        Assert.Contains(result.Program.WitImportMethods, method =>
            method.WitImport is { InterfaceName: ReactorHost, FunctionName: "watch" });
        Assert.Contains(result.Program.WitImportMethods, method =>
            method.WitImport is { InterfaceName: ReactorHost, FunctionName: "cancel" });
    }

    private static string Compile(TestAssets assets, string name, string source, bool optimize) =>
        optimize ? assets.CompileOptimizedSource(name, source) : assets.CompileSource(name, source);

    private static CompilationResult CompileAsynchronousMain(
        TestAssets assets, string name, string source, bool optimize, WasmTarget target)
    {
        var assembly = Path.Combine(assets.Directory, $"{name}.dll");
        var emitted = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(assets.CoreLib)],
            new CSharpCompilationOptions(
                OutputKind.ConsoleApplication,
                optimizationLevel: optimize ? OptimizationLevel.Release : OptimizationLevel.Debug))
            .Emit(assembly);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        var (typeName, token) = ReadAsynchronousMain(assembly);
        return NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            typeName,
            "Main",
            [],
            target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0",
            EntryMethodToken: token,
            EntryPointKind: CompilerEntryPointKind.ManagedExecutable));
    }

    // Roslyn's <Main> wrapper is the image entry point; the compiler enters the async Main itself.
    private static (string TypeName, int Token) ReadAsynchronousMain(string assembly)
    {
        using var stream = File.OpenRead(assembly);
        using var image = new PEReader(stream);
        var metadata = image.GetMetadataReader();
        var entry = metadata.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.EntityHandle(
            image.PEHeaders.CorHeader!.EntryPointTokenOrRelativeVirtualAddress));
        var type = metadata.GetTypeDefinition(entry.GetDeclaringType());
        var main = type.GetMethods().Single(method =>
            metadata.GetString(metadata.GetMethodDefinition(method).Name) == "Main");
        var @namespace = metadata.GetString(type.Namespace);
        var name = metadata.GetString(type.Name);
        return (string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}", MetadataTokens.GetToken(main));
    }

    // Returns 42 on desktop .NET too. Completion order among simultaneous delays is the oracle's
    // simulated time, so only the order the program fixes is checked.
    private const string AsyncMainSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;

        namespace CommandWorldAsyncMainFixture;

        internal static class Program
        {
            public static async Task<int> Main()
            {
                var order = new List<int>();
                await Task.Yield();
                order.Add(1);
                await Task.Delay(5);
                order.Add(2);
                var results = await Task.WhenAll(Step(order, 3, 10), Step(order, 0, 20), Step(order, 1, 30));
                if (results[0] + results[1] + results[2] != 60) return 1;
                await Task.Delay(1).ConfigureAwait(false);
                using (var timeout = new CancellationTokenSource())
                {
                    timeout.CancelAfter(1);
                    try
                    {
                        await Task.Delay(Timeout.Infinite, timeout.Token);
                        return 2;
                    }
                    catch (TaskCanceledException)
                    {
                        order.Add(9);
                    }
                }
                try
                {
                    await FailAsync();
                    return 3;
                }
                catch (InvalidOperationException exception) when (exception.Message == "expected")
                {
                }
                return order.Count == 6 && order[0] == 1 && order[1] == 2 && order[5] == 9 ? 42 : 4;
            }

            private static async Task<int> Step(List<int> order, int delay, int value)
            {
                await Task.Delay(delay);
                order.Add(value);
                return value;
            }

            private static async Task FailAsync()
            {
                await Task.Yield();
                throw new InvalidOperationException("expected");
            }
        }
        """;

    private const string StalledMainSource = """
        using System.Threading.Tasks;

        namespace CommandWorldStalledMainFixture;

        internal static class Program
        {
            public static async Task<int> Main()
            {
                await new TaskCompletionSource<int>().Task;
                return 0;
            }
        }
        """;

    private const string RunSource = """
        namespace SyncAwaitFixture;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var failure = Checks.Run();
                return failure == 0 ? input + 1 : failure;
            }
        }
        """ + ChecksSource;

    private const string MainSource = """
        namespace SyncAwaitFixture;

        public static class Program
        {
            // The process exit code is the first failing check, or 0.
            public static int Main() => Checks.Run();
        }
        """ + ChecksSource;

    // Expected values are desktop .NET's; the deferred-work block is NetWasm's and Mono WASI's
    // single-threaded behaviour, where desktop runs the work on its thread pool instead.
    internal const string ChecksSource = """

        public static class Checks
        {
            public static int Run()
            {
                if (CompletedTaskAsync().Result != 1) return 1;
                if (CompletedValueTaskAsync().Result != 2) return 2;

                var source = new System.Threading.Tasks.TaskCompletionSource<int>();
                var pending = AddAfterAsync(source.Task, 10);
                if (pending.IsCompleted) return 3;
                source.SetResult(30);
                if (!pending.IsCompletedSuccessfully || pending.Result != 40) return 4;

                var failing = new System.Threading.Tasks.TaskCompletionSource<int>();
                var faulted = AddAfterAsync(failing.Task, 1);
                failing.SetException(new System.InvalidOperationException("boom"));
                if (!faulted.IsFaulted ||
                    faulted.Exception!.InnerException is not System.InvalidOperationException { Message: "boom" }) return 5;

                var configured = new System.Threading.Tasks.TaskCompletionSource<int>();
                var configuredTask = DoubleConfiguredAsync(configured.Task);
                configured.SetResult(21);
                if (configuredTask.Result != 42) return 6;

                var valueSource = new ValueSource();
                var valueTask = AddAfterValueAsync(valueSource, 5);
                if (valueTask.IsCompleted) return 7;
                valueSource.Complete(37);
                if (!valueTask.IsCompletedSuccessfully || valueTask.Result != 42) return 8;

                var cancellation = new System.Threading.CancellationTokenSource();
                var cancelSource = new System.Threading.Tasks.TaskCompletionSource<int>();
                cancellation.Token.Register(() => cancelSource.TrySetCanceled(cancellation.Token));
                var canceled = AddAfterAsync(cancelSource.Task, 1);
                cancellation.Cancel();
                if (!canceled.IsCanceled) return 9;

                if (!System.Threading.Tasks.Task.Delay(0).IsCompletedSuccessfully) return 10;

        #if !DESKTOP_ORACLE
                var yielded = YieldThenAsync();
                if (yielded.IsCompleted) return 11;
                var delayed = System.Threading.Tasks.Task.Delay(5);
                if (delayed.IsCompleted) return 12;
                try
                {
                    delayed.GetAwaiter().GetResult();
                    return 13;
                }
                catch (System.PlatformNotSupportedException)
                {
                }
                using (var timeout = new System.Threading.CancellationTokenSource(5))
                {
                    if (timeout.IsCancellationRequested) return 14;
                }
        #endif
                return 0;
            }

            private static async System.Threading.Tasks.Task<int> CompletedTaskAsync()
            {
                await System.Threading.Tasks.Task.CompletedTask;
                return await System.Threading.Tasks.Task.FromResult(1);
            }

            private static async System.Threading.Tasks.Task<int> CompletedValueTaskAsync()
            {
                await new System.Threading.Tasks.ValueTask();
                return await new System.Threading.Tasks.ValueTask<int>(2);
            }

            private static async System.Threading.Tasks.Task<int> AddAfterAsync(
                System.Threading.Tasks.Task<int> antecedent, int addend) => await antecedent + addend;

            private static async System.Threading.Tasks.Task<int> DoubleConfiguredAsync(
                System.Threading.Tasks.Task<int> antecedent) => await antecedent.ConfigureAwait(false) * 2;

            private static async System.Threading.Tasks.Task<int> AddAfterValueAsync(ValueSource source, int addend) =>
                await new System.Threading.Tasks.ValueTask<int>(source, source.Version) + addend;

            private static async System.Threading.Tasks.Task YieldThenAsync()
            {
                await System.Threading.Tasks.Task.Yield();
            }
        }

        public sealed class ValueSource : System.Threading.Tasks.Sources.IValueTaskSource<int>
        {
            private System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore<int> _core;

            public short Version => _core.Version;

            public void Complete(int value) => _core.SetResult(value);

            public int GetResult(short token) => _core.GetResult(token);

            public System.Threading.Tasks.Sources.ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

            public void OnCompleted(
                System.Action<object?> continuation,
                object? state,
                short token,
                System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags flags) =>
                _core.OnCompleted(continuation, state, token, flags);
        }
        """;
}
