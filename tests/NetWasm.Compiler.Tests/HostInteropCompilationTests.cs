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
                target) with
            { UseJavaScriptExportBoundary = true });

            CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);

            Assert.Collection(
                result.InteropManifest.Exports,
                export =>
                {
                    Assert.Equal("task_value", export.Name);
                    Assert.Equal(["input"], export.ParameterNames.ToArray());
                    Assert.Equal("task", export.AsyncReturn);
                    Assert.Equal("i32", export.Result);
                    Assert.NotNull(export.StatusExport);
                    Assert.NotNull(export.ResultExport);
                    Assert.NotNull(export.CompleteExport);
                    Assert.Equal("exception-handle-v1", export.CompletionResult);
                },
                export =>
                {
                    Assert.Equal("value_task_value", export.Name);
                    Assert.Equal("value-task", export.AsyncReturn);
                    Assert.Equal("i32", export.Result);
                    Assert.NotNull(export.StatusExport);
                    Assert.NotNull(export.ResultExport);
                    Assert.NotNull(export.CompleteExport);
                    Assert.Equal("exception-handle-v1", export.CompletionResult);
                });
        }
    }

    [Fact]
    public void CompilerRecordsExportParameterNamesAndNullability()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ExportNullabilityFixture",
            """
            using System.Runtime.InteropServices.JavaScript;

            public static class EntryPoint
            {
                public static int Run(int input) => input;

            #nullable enable
                [JSExport("greet")]
                public static string Greet(string name, string? title, int count) => name;

                [JSExport("maybe")]
                public static string? Maybe(byte[] data) => null;
            #nullable disable

                [JSExport("oblivious")]
                public static string Oblivious(string value) => value;
            }
            """);

        var result = NetWasmCompiler.Compile(CompilerTestSupport.CreateReactorOptions(
            assets, assembly, "EntryPoint", "Run", WasmTarget.Wasm32) with
        { UseJavaScriptExportBoundary = true });

        var exports = result.InteropManifest.Exports.ToDictionary(export => export.Name);
        Assert.Equal(["name", "title", "count"], exports["greet"].ParameterNames.ToArray());
        Assert.Equal([true, false, true], exports["greet"].NonNullableParameters.ToArray());
        Assert.True(exports["greet"].NonNullableResult);
        Assert.Equal([true], exports["maybe"].NonNullableParameters.ToArray());
        Assert.False(exports["maybe"].NonNullableResult);
        // Without nullable annotations a reference stays nullable.
        Assert.Equal([false], exports["oblivious"].NonNullableParameters.ToArray());
        Assert.False(exports["oblivious"].NonNullableResult);
    }

    // Issue #120: as in .NET, a JSExport in a referenced library is exported,
    // so a framework library can ship the callbacks its JavaScript host calls.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CompilerExportsJSExportsDeclaredInReferencedLibraries(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSource(
            "ExportLibrary",
            """
            using System.Runtime.InteropServices.JavaScript;

            public static partial class LibraryExports
            {
                [JSExport("libraryCallback")]
                public static void LibraryCallback(float width, float height)
                {
                }

                [JSExport]
                public static int Twice(int value) => value * 2;
            }
            """);
        var assembly = assets.CompileSource(
            "ExportApplication",
            """
            using System.Runtime.InteropServices.JavaScript;

            public static class EntryPoint
            {
                public static int Run(int input) => input;

                [JSExport("appCallback")]
                public static int AppCallback(int value) => value + 1;
            }
            """,
            library);
        var options = CompilerTestSupport.CreateReactorOptions(assets, assembly, "EntryPoint", "Run", target);

        var result = NetWasmCompiler.Compile(options with
        {
            ReferencePaths = [.. options.ReferencePaths, library],
            UseJavaScriptExportBoundary = true,
        });

        var exports = result.InteropManifest.Exports.ToDictionary(export => export.Name);
        Assert.Equal(["Twice", "appCallback", "libraryCallback"], exports.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["width", "height"], exports["libraryCallback"].ParameterNames.ToArray());
    }

    [Fact]
    public void CompilerRejectsAJSExportNameDeclaredByTwoAssemblies()
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSource(
            "ResizeLibrary",
            """
            using System.Runtime.InteropServices.JavaScript;

            public static partial class LibraryExports
            {
                [JSExport("resize")]
                public static void OnResize(int width) { }
            }
            """);
        var assembly = assets.CompileSource(
            "ResizeApplication",
            """
            using System.Runtime.InteropServices.JavaScript;

            public static class EntryPoint
            {
                public static int Run(int input) => input;

                [JSExport("resize")]
                public static void Resize(int width) { }
            }
            """,
            library);
        var options = CompilerTestSupport.CreateReactorOptions(assets, assembly, "EntryPoint", "Run");

        var error = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options with
        {
            ReferencePaths = [.. options.ReferencePaths, library],
            UseJavaScriptExportBoundary = true,
        }));

        Assert.Contains("duplicate JSExport name 'resize'", error.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("in ResizeApplication", error.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("in ResizeLibrary", error.Diagnostic.Message, StringComparison.Ordinal);
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

    // A [WasmImportLinkage] declaration imports its entry point from the module
    // its library names, which the host supplies; no linked archive defines it.
    [Fact]
    public void CompilerImportsWasmImportLinkageDeclarationsFromTheHost()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "WasmImportLinkageFixture",
            """
            using System.Runtime.InteropServices;

            public static class HostUi
            {
                [DllImport("host_ui", EntryPoint = "create_node"), WasmImportLinkage]
                public static extern ulong CreateNode(uint type);
            }

            public static class EntryPoint
            {
                public static int Run(int input) => (int)HostUi.CreateNode((uint)input);
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [])
        {
            ImportModules = ["host_gl"],
        });

        var import = Assert.Single(result.FunctionImports, candidate => candidate.Name == "create_node");
        Assert.Equal("host_ui", import.Module);
        Assert.Equal(CliValueKind.I8, import.Type.Result);
        Assert.Equal([CliValueKind.I4], import.Type.Parameters.ToArray());
        Assert.Equal(["host_gl", "host_ui"], result.InteropManifest.ImportModules.ToArray());
        Assert.DoesNotContain(result.NativeImports, native => native.EntryPoint == "create_node");
        Assert.Empty(result.InteropManifest.Imports);
    }

    [Theory]
    [InlineData("netwasm.runtime.v1")]
    [InlineData("wasi:io/streams@0.2.0")]
    [InlineData("wasi_snapshot_preview1")]
    public void CompilerRejectsReservedWasmImportLinkageModules(string module)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ReservedWasmImportLinkageFixture",
            $$"""
            using System.Runtime.InteropServices;

            public static class Host
            {
                [DllImport("{{module}}", EntryPoint = "read"), WasmImportLinkage]
                public static extern int Read();
            }

            public static class EntryPoint
            {
                public static int Run(int input) => Host.Read();
            }
            """);

        var error = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [])));
        Assert.Contains("[WasmImportLinkage]", error.Message);
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
        // A scalar import takes its arguments and returns its result directly.
        Assert.Equal(JavaScriptDirectImport.DirectCall, import.Call);
        var physicalImport = result.FunctionImports.Single(candidate =>
            candidate.Module == import.Module && candidate.Name == import.Name);
        Assert.Equal([CliValueKind.I4, CliValueKind.I4], physicalImport.Type.Parameters.ToArray());
        Assert.Equal(CliValueKind.I4, physicalImport.Type.Result);
        var echoImport = result.InteropManifest.Imports[1];
        Assert.Equal(JavaScriptDirectImport.StatusCall, echoImport.Call);
        var echoPhysical = result.FunctionImports.Single(candidate =>
            candidate.Module == echoImport.Module && candidate.Name == echoImport.Name);
        Assert.Equal(CliValueKind.ManagedAddress, echoPhysical.Type.Parameters[^1]);
        Assert.Equal(CliValueKind.I4, echoPhysical.Type.Result);
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
        Assert.Equal(JavaScriptDirectImport.DirectCall, reportImport.Call);
        Assert.Equal(CliValueKind.Void, result.FunctionImports.Single(candidate =>
            candidate.Module == reportImport.Module && candidate.Name == reportImport.Name).Type.Result);
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
    public void CompilerBuildsSynchronousStringJSExport()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "StringHostExportSignatureFixture",
            """
            using System.Runtime.InteropServices.JavaScript;

            public static class EntryPoint
            {
                public static int Run(int input) => input;

                [JSExport("echo")]
                public static string Echo(string left, string right) => left + right;

                [JSExport("value")]
                public static string Value() => "result";
            }
            """);

        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
        {
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "EntryPoint",
                "Run",
                [],
                target));

            var export = Assert.Single(result.InteropManifest.Exports,
                candidate => candidate.Name == "echo");
            Assert.Equal("echo", export.Name);
            Assert.Equal(["string", "string"], export.Parameters.ToArray());
            Assert.Equal("string", export.Result);
            var resultOnly = Assert.Single(result.InteropManifest.Exports,
                candidate => candidate.Name == "value");
            Assert.Empty(resultOnly.Parameters);
            Assert.Equal("string", resultOnly.Result);
            Assert.Contains(result.FunctionImports,
                import => import.Module == "netwasm.host.v1" &&
                    import.Name == "interop_string_length");
            Assert.Contains(result.FunctionImports,
                import => import.Module == "netwasm.host.v1" &&
                    import.Name == "interop_copy_string_utf16");
        }
    }

    [Fact]
    public void CompilerEmitsByteArrayJavaScriptExportBoundary()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ByteArrayHostExportSignatureFixture",
            """
            using System.Runtime.InteropServices.JavaScript;

            public static class EntryPoint
            {
                public static int Run(int input) => input;

                [JSExport("echo_bytes")]
                public static byte[] EchoBytes(byte[] input) => input;
            }
            """);

        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
        {
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "EntryPoint",
                "Run",
                [],
                target));

            var export = Assert.Single(result.InteropManifest.Exports,
                candidate => candidate.Name == "echo_bytes");
            Assert.Equal(["bytes"], export.Parameters.ToArray());
            Assert.Equal("bytes", export.Result);
            Assert.Contains(result.FunctionImports,
                import => import.Module == "netwasm.host.v1" &&
                    import.Name == "interop_byte_length");
            Assert.Contains(result.FunctionImports,
                import => import.Module == "netwasm.host.v1" &&
                    import.Name == "interop_copy_bytes");
        }
    }

    [Fact]
    public void CompilerEmitsJSObjectJavaScriptExportBoundary()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "HostObjectExportFixture",
            """
            using System.Runtime.InteropServices.JavaScript;

            public static class EntryPoint
            {
                public static int Run(int input) => input;

                [JSExport("pick")]
                public static JSObject? Pick(JSObject? first, JSObject? second) => first ?? second;
            }
            """);

        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
        {
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "EntryPoint",
                "Run",
                [],
                target,
                UseJavaScriptExportBoundary: true));

            var export = Assert.Single(result.InteropManifest.Exports,
                candidate => candidate.Name == "pick");
            Assert.Equal(["object", "object"], export.Parameters.ToArray());
            Assert.Equal("object", export.Result);
            Assert.Contains(result.FunctionImports,
                import => import.Module == "netwasm.host.v1" &&
                    import.Name == "interop_release_handle");
        }
    }

    [Fact]
    public void CompilerEmitsTypedArrayJavaScriptBoundaries()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "TypedArrayHostBoundaryFixture",
            """
            using System;
            using System.Runtime.InteropServices.JavaScript;

            public delegate int SampleCallback(ushort[] samples);

            public static class Host
            {
                [JSImport("scale", "consumer.arrays")]
                public static extern long[] Scale(float[] values);

                [JSImport("watch", "consumer.arrays")]
                public static extern JSSubscription Watch(SampleCallback callback);
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    using var subscription = Host.Watch(samples => samples.Length);
                    return Host.Scale([1f, 2f]).Length + input;
                }

                [JSExport("average")]
                public static double[] Average(int[] values, sbyte[] signs, uint[] flags, ulong[] ids, short[] deltas) =>
                    [values.Length + signs.Length + flags.Length + ids.Length + deltas.Length];
            }
            """);

        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
        {
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "EntryPoint",
                "Run",
                [],
                target));

            CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
            var export = Assert.Single(result.InteropManifest.Exports, candidate => candidate.Name == "average");
            Assert.Equal(["i32[]", "i8[]", "u32[]", "u64[]", "i16[]"], export.Parameters.ToArray());
            Assert.Equal("f64[]", export.Result);
            var scale = Assert.Single(result.InteropManifest.Imports, candidate => candidate.Name == "scale");
            Assert.Equal(["f32[]"], scale.Parameters.ToArray());
            Assert.Equal("i64[]", scale.Result);
            Assert.Equal(["u16[]"], Assert.Single(result.InteropManifest.Callbacks).Parameters.ToArray());
            Assert.Contains(result.FunctionImports,
                import => import.Module == "netwasm.host.v1" && import.Name == "interop_byte_length");
            Assert.Contains(result.FunctionImports,
                import => import.Module == "netwasm.host.v1" && import.Name == "interop_copy_bytes");
        }
    }

    [Fact]
    public void CompilerPassesSpanJSImportParametersAsMemoryViews()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "SpanViewHostBoundaryFixture",
            """
            using System;
            using System.Runtime.InteropServices.JavaScript;

            public static class Host
            {
                [JSImport("upload", "consumer.views")]
                public static extern int Upload(Span<byte> pixels, ReadOnlySpan<double> samples, Span<long> ids, int count);
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Span<long> ids = stackalloc long[2];
                    return Host.Upload(new byte[input], new[] { 1.5, 2.5 }, ids, input);
                }
            }
            """);

        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
        {
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "EntryPoint",
                "Run",
                [],
                target));

            CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
            var upload = Assert.Single(result.InteropManifest.Imports, candidate => candidate.Name == "upload");
            Assert.Equal(["u8-view", "f64-view", "i64-view", "i32"], upload.Parameters.ToArray());
            Assert.Equal("i32", upload.Result);
            Assert.Equal(0, result.InteropManifest.TargetLayout.SpanReferenceOffset);
            Assert.Equal(target == WasmTarget.Wasm32 ? 4 : 8, result.InteropManifest.TargetLayout.SpanLengthOffset);
            // Each span passes the address of its copy; the value frame comes last.
            var import = Assert.Single(result.FunctionImports, candidate => candidate.Module == "consumer.views");
            Assert.Equal(
                [CliValueKind.ManagedAddress, CliValueKind.ManagedAddress, CliValueKind.ManagedAddress,
                    CliValueKind.I4, CliValueKind.ManagedAddress],
                import.Type.Parameters.ToArray());
        }
    }

    [Fact]
    public void CompilerRejectsSpanParametersOnAsynchronousJSImport()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "AsyncSpanImportFixture",
            """
            using System;
            using System.Runtime.InteropServices.JavaScript;
            using System.Threading.Tasks;

            public static class Host
            {
                [JSImport("upload", "consumer.views")]
                public static extern Task<int> Upload(ReadOnlySpan<byte> pixels);
            }

            public static class EntryPoint
            {
                public static int Run(int input) => Host.Upload(new byte[input]).Result;
            }
            """);

        var exception = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(
            new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [])));

        Assert.Contains("an asynchronous JSImport cannot take them", exception.Message);
    }

    [Fact]
    public void CompilerRejectsSpanJSExportParameters()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "SpanExportFixture",
            """
            using System;
            using System.Runtime.InteropServices.JavaScript;

            public static class EntryPoint
            {
                public static int Run(int input) => input;

                [JSExport("measure")]
                public static int Measure(Span<byte> pixels) => pixels.Length;
            }
            """);

        var exception = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(
            new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [])));

        Assert.Contains("JSExport currently supports", exception.Message);
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
