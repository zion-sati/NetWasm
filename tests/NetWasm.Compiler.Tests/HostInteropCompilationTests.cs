using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class HostInteropCompilationTests
{
    [Fact]
    public void CompilerBuildsIdiomaticTaskAndValueTaskImports()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "AsyncHostInteropFixture",
            """
            using System.Runtime.InteropServices.JavaScript;
            using System.Threading.Tasks;

            public static class Host
            {
                [JSImport("task_value", "consumer.async")]
                public static extern Task<int> TaskValue(int input);

                [JSImport("value_task_value", "consumer.async")]
                public static extern ValueTask<int> ValueTaskValue(int input);
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var task = Host.TaskValue(input);
                    var valueTask = Host.ValueTaskValue(input);
                    return task.IsCompleted || valueTask.IsCompleted ? 1 : 0;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(CompilerTestSupport.CreateReactorOptions(
            assets,
            assembly,
            "EntryPoint",
            "Run"));

        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);

        Assert.Collection(
            result.InteropManifest.Imports,
            import =>
            {
                Assert.Equal("task_value", import.Name);
                Assert.Equal("task", import.AsyncReturn);
                Assert.Equal("i32", import.Result);
                Assert.NotNull(import.ResolveExport);
                Assert.NotNull(import.RejectExport);
            },
            import =>
            {
                Assert.Equal("value_task_value", import.Name);
                Assert.Equal("value-task", import.AsyncReturn);
                Assert.Equal("i32", import.Result);
                Assert.NotNull(import.ResolveExport);
                Assert.NotNull(import.RejectExport);
            });
    }

    [Fact]
    public void CompilerBuildsIdiomaticTaskAndValueTaskExports()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "AsyncHostExportFixture",
            """
            using System.Runtime.InteropServices.JavaScript;
            using System.Threading.Tasks;

            public static class EntryPoint
            {
                public static int Run(int input) => input;

                [JSExport("task_value")]
                public static Task<int> TaskValue(int input) =>
                    Task<int>.FromResult(input + 1);

                [JSExport("value_task_value")]
                public static ValueTask<int> ValueTaskValue(int input) =>
                    new(input + 2);
            }
            """);

        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
        {
            var result = NetWasmCompiler.Compile(CompilerTestSupport.CreateReactorOptions(
                assets,
                assembly,
                "EntryPoint",
                "Run",
                target));

            CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);

            Assert.Collection(
                result.InteropManifest.Exports,
                export =>
                {
                    Assert.Equal("task_value", export.Name);
                    Assert.Equal("task", export.AsyncReturn);
                    Assert.Equal("i32", export.Result);
                    Assert.NotNull(export.StatusExport);
                    Assert.NotNull(export.ResultExport);
                    Assert.NotNull(export.CompleteExport);
                },
                export =>
                {
                    Assert.Equal("value_task_value", export.Name);
                    Assert.Equal("value-task", export.AsyncReturn);
                    Assert.Equal("i32", export.Result);
                    Assert.NotNull(export.StatusExport);
                    Assert.NotNull(export.ResultExport);
                    Assert.NotNull(export.CompleteExport);
                });
        }
    }

    [Fact]
    public void CompilerBuildsPromiseImportWithSuccessAndFailureCallbacks()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "PromiseHostInteropFixture",
            """
            using System;
            using System.Runtime.InteropServices.JavaScript;

            public static class EntryPoint
            {
                [JSImportPromise("value_async", "consumer.async")]
                private static extern JSSubscription Begin(
                    int input,
                    Action<int> resolve,
                    Action reject);

                public static int Run(int input)
                {
                    var subscription = Begin(input, Resolve, Reject);
                    subscription.Dispose();
                    return input;
                }

                private static void Resolve(int value) { }
                private static void Reject() { }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            []));

        var import = Assert.Single(result.InteropManifest.Imports);
        Assert.Equal("promise", import.Result);
        Assert.True(import.Parameters.AsSpan().SequenceEqual(
            ["i32", "callback", "callback"]));
        Assert.Equal(2, result.InteropManifest.Callbacks.Length);
        Assert.Contains(result.InteropManifest.Callbacks,
            callback => callback.Parameters.AsSpan().SequenceEqual(["i32"]));
        Assert.Contains(result.InteropManifest.Callbacks,
            callback => callback.Parameters.IsEmpty);
    }

    [Fact]
    public void CompilerEmitsCompleteBrowserHostFixtureForBothTargets()
    {
        using var assets = TestAssets.Create();
        var source = File.ReadAllText(Path.Combine(
            assets.Root,
            "tests",
            "end-to-end",
            "host-interop",
            "javascript",
            "ScalarInterop.cs"));
        var assembly = assets.CompileSource("CompleteHostInteropFixture", source);

        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
        {
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "EntryPoint",
                "Run",
                [],
                target));

            Assert.NotEmpty(result.ApplicationModule);
            Assert.Equal(target == WasmTarget.Wasm32 ? "wasm32" : "wasm64",
                result.InteropManifest.Target);
            Assert.Contains(result.InteropManifest.Imports,
                import => import.Result == "f64");
            Assert.Contains(result.InteropManifest.Imports,
                import => import.Result == "u64");
            Assert.Contains(result.InteropManifest.Imports,
                import => import.Result == "subscription");
            Assert.Contains(result.InteropManifest.Callbacks,
                callback => callback.Parameters.Contains("string"));
            Assert.Contains(result.InteropManifest.Callbacks,
                callback => callback.Parameters.Contains("bytes"));
        }
    }

    [Fact]
    public void CompilerBuildsDeterministicScalarHostInteropManifest()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "HostInteropFixture",
            """
            using System.Runtime.InteropServices.JavaScript;

            public static class HostMath
            {
                [JSImport("add", "consumer.math")]
                public static extern int Add(int left, int right);

                [JSImport("report", "consumer.math")]
                public static extern void Report(int value);

                [JSImport("receive", "consumer.math")]
                public static extern void Receive(string? value);

                [JSImport("echo", "consumer.math")]
                public static extern string? Echo(string? value);
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    HostMath.Report(input);
                    HostMath.Receive("a\0Ω\ud800");
                    return HostMath.Echo(null) is null ? HostMath.Add(input, 2) : 0;
                }

                [JSExport("twice")]
                public static int Twice(int input) => input * 2;
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            WasmTarget.Wasm64));

        Assert.Equal(4, result.InteropManifest.Imports.Length);
        var import = result.InteropManifest.Imports[0];
        Assert.Equal("wasm64", result.InteropManifest.Target);
        Assert.Equal(0, result.InteropManifest.StatusAbi.SuccessStatus);
        Assert.Equal(1, result.InteropManifest.StatusAbi.HostFailureStatus);
        Assert.Equal(0, result.InteropManifest.StatusAbi.ScalarResultOffset);
        Assert.Equal(8, result.InteropManifest.TargetLayout.ManagedReferenceSize);
        Assert.Equal(8, result.InteropManifest.TargetLayout.StringLengthOffset);
        Assert.Equal(12, result.InteropManifest.TargetLayout.StringDataOffset);
        Assert.Equal("consumer.math", import.Module);
        Assert.Equal("add", import.Name);
        Assert.Equal(2, import.Parameters.Length);
        Assert.Equal("i32", import.Parameters[0]);
        Assert.Equal("i32", import.Parameters[1]);
        Assert.Equal("i32", import.Result);
        var physicalImport = result.FunctionImports.Single(candidate =>
            candidate.Module == import.Module && candidate.Name == import.Name);
        Assert.Equal([CliValueKind.I4, CliValueKind.I4, CliValueKind.ManagedAddress],
            physicalImport.Type.Parameters.ToArray());
        Assert.Equal(CliValueKind.I4, physicalImport.Type.Result);
        var echoImport = result.InteropManifest.Imports[1];
        Assert.Equal("echo", echoImport.Name);
        Assert.Equal("string", Assert.Single(echoImport.Parameters));
        Assert.Equal("string", echoImport.Result);
        var receiveImport = result.InteropManifest.Imports[2];
        Assert.Equal("receive", receiveImport.Name);
        Assert.Equal("void", receiveImport.Result);
        Assert.Equal("string", Assert.Single(receiveImport.Parameters));
        var reportImport = result.InteropManifest.Imports[3];
        Assert.Equal("report", reportImport.Name);
        Assert.Equal("void", reportImport.Result);
        var export = Assert.Single(result.InteropManifest.Exports);
        Assert.Equal("twice", export.Name);
        Assert.Equal("i32", Assert.Single(export.Parameters));
        Assert.Equal("i32", export.Result);
    }

    [Fact]
    public void CompilerRejectsJSImportWithManagedBody()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "InvalidHostInteropFixture",
            """
            using System.Runtime.InteropServices.JavaScript;

            public static class EntryPoint
            {
                [JSImport("bad", "consumer.math")]
                public static int Bad(int input) => input;

                public static int Run(int input) => Bad(input);
            }
            """);

        var exception = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(
            new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "EntryPoint",
                "Run",
                [])));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Contains("JSImport must be a bodyless static method", exception.Message);
    }

    [Fact]
    public void CompilerRejectsInstanceJSExport()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "InvalidHostExportFixture",
            """
            using System.Runtime.InteropServices.JavaScript;

            public sealed class EntryPoint
            {
                public static int Run(int input) => input;

                [JSExport("bad")]
                public int Bad(int input) => input;
            }
            """);

        var exception = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(
            new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [])));

        Assert.Contains("JSExport must be a static method with a CIL body", exception.Message);
    }

    [Fact]
    public void CompilerRejectsNonScalarJSExport()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "InvalidHostExportSignatureFixture",
            """
            using System.Runtime.InteropServices.JavaScript;

            public static class EntryPoint
            {
                public static int Run(int input) => input;

                [JSExport("bad")]
                public static string Bad(string input) => input;
            }
            """);

        var exception = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(
            new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [])));

        Assert.Contains("JSExport currently supports only primitive scalar", exception.Message);
    }

    [Fact]
    public void CompilerRejectsCallbackWithoutSubscriptionResult()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "InvalidCallbackResultFixture",
            """
            using System.Runtime.InteropServices.JavaScript;

            public delegate int Transform(int value);

            public static class Host
            {
                [JSImport("bad", "consumer.callback")]
                public static extern int Bad(Transform callback);
            }

            public static class EntryPoint
            {
                public static int Run(int input) => Host.Bad(Double);
                private static int Double(int input) => input * 2;
            }
            """);

        var exception = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(
            new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [])));

        Assert.Contains("callbacks require exactly one delegate parameter", exception.Message);
    }

    [Fact]
    public void CompilerRejectsJSImportWithoutExplicitConsumerModule()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ImplicitModuleHostInteropFixture",
            """
            using System.Runtime.InteropServices.JavaScript;

            public static class EntryPoint
            {
                [JSImport("bad")]
                public static extern int Bad(int input);

                public static int Run(int input) => Bad(input);
            }
            """);

        var exception = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(
            new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "EntryPoint",
                "Run",
                [])));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Contains("requires an explicit consumer module name", exception.Message);
    }

    [Fact]
    public void CompilerBuildsOpaqueJSObjectImports()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "HostObjectInteropFixture",
            """
            using System.Runtime.InteropServices.JavaScript;

            public static class HostObjects
            {
                [JSImport("create", "consumer.objects")]
                public static extern JSObject? Create();

                [JSImport("value", "consumer.objects")]
                public static extern int Value(JSObject? value);
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = HostObjects.Create();
                    if (value is null)
                    {
                        return 0;
                    }
                    var result = HostObjects.Value(value);
                    value.Dispose();
                    return result;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            []));

        var create = result.InteropManifest.Imports.Single(import => import.Name == "create");
        Assert.Equal("object", create.Result);
        var value = result.InteropManifest.Imports.Single(import => import.Name == "value");
        Assert.Equal("object", Assert.Single(value.Parameters));
        Assert.Equal("i32", value.Result);
    }

    [Fact]
    public void CompilerBuildsManagedCallbackSubscriptionThunk()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "HostCallbackInteropFixture",
            """
            using System.Runtime.InteropServices.JavaScript;

            public delegate int Transform(int value);

            public static class HostCallbacks
            {
                [JSImport("subscribe", "consumer.callbacks")]
                public static extern JSSubscription Subscribe(Transform callback);
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var subscription = HostCallbacks.Subscribe(Double);
                    subscription.Dispose();
                    return input;
                }

                private static int Double(int value) => value * 2;
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            []));

        var import = Assert.Single(result.InteropManifest.Imports);
        Assert.Equal("callback", Assert.Single(import.Parameters));
        Assert.Equal("subscription", import.Result);
        var callback = Assert.Single(result.InteropManifest.Callbacks);
        Assert.Equal("consumer.callbacks", callback.Module);
        Assert.Equal("subscribe", callback.ImportName);
        Assert.Equal(0, callback.ParameterIndex);
        Assert.Equal("i32", Assert.Single(callback.Parameters));
        Assert.Equal("i32", callback.Result);
        Assert.StartsWith("netwasm.callback.", callback.ExportName, StringComparison.Ordinal);
    }

    [Fact]
    public void CompilerMapsNativeIntegerInteropToTheSelectedTargetWidth()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "NativeIntegerInteropFixture",
            """
            using System.Runtime.InteropServices.JavaScript;

            public static class NativeHost
            {
                [JSImport("round_trip", "consumer.native")]
                public static extern nint RoundTrip(nint value);
            }

            public static class EntryPoint
            {
                public static int Run(int input) => (int)NativeHost.RoundTrip((nint)input);
            }
            """);

        var wasm32 = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            WasmTarget.Wasm32));
        var wasm64 = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            WasmTarget.Wasm64));

        var wasm32Import = Assert.Single(wasm32.InteropManifest.Imports);
        var wasm64Import = Assert.Single(wasm64.InteropManifest.Imports);
        Assert.Equal("i32", Assert.Single(wasm32Import.Parameters));
        Assert.Equal("i32", wasm32Import.Result);
        Assert.Equal("i64", Assert.Single(wasm64Import.Parameters));
        Assert.Equal("i64", wasm64Import.Result);
    }

    [Fact]
    public void CompilerRejectsDuplicateExportsAndUnsupportedHostSignaturesDeterministically()
    {
        using var assets = TestAssets.Create();
        var duplicateAssembly = assets.CompileSource(
            "DuplicateExportInteropFixture",
            """
            using System.Runtime.InteropServices.JavaScript;

            public static class EntryPoint
            {
                public static int Run(int input) => input;

                [JSExport("duplicate")]
                public static int First(int input) => input;

                [JSExport("duplicate")]
                public static int Second(int input) => input;
            }
            """);
        var duplicate = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(
            new CompilerOptions(duplicateAssembly, [assets.CoreLib], "EntryPoint", "Run", [])));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, duplicate.Diagnostic.Code);
        Assert.Contains("duplicate JSExport name 'duplicate'", duplicate.Message);

        var unsupportedAssembly = assets.CompileSource(
            "UnsupportedInteropSignatureFixture",
            """
            using System.Runtime.InteropServices.JavaScript;

            public static class Host
            {
                [JSImport("bad", "consumer.invalid")]
                public static extern object Bad(object value);
            }

            public static class EntryPoint
            {
                public static int Run(int input) => input;
            }
            """);
        var unsupported = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(
            new CompilerOptions(unsupportedAssembly, [assets.CoreLib], "EntryPoint", "Run", [])));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, unsupported.Diagnostic.Code);
        Assert.Contains("JSImport currently supports primitive scalars", unsupported.Message);
    }
}
