using System.Text.Json;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

using NetWasm.Compiler.ExceptionTypes;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ExceptionTerminationCompilationTests
{
    [Fact]
    public void DesktopObjectDisposedExceptionThrowIfPreservesUpstreamObjectNames()
    {
        var objectException = Assert.Throws<ObjectDisposedException>(() =>
            ObjectDisposedException.ThrowIf(true, new object()));
        Assert.Equal("System.Object", objectException.ObjectName);

        var typeException = Assert.Throws<ObjectDisposedException>(() =>
            ObjectDisposedException.ThrowIf(true, typeof(object)));
        Assert.Equal("System.Object", typeException.ObjectName);
    }

    [Fact]
    public void SupportedExceptionConstructorsUseDesktopHResults()
    {
#pragma warning disable CA2201 // This is an explicit constructor compatibility oracle.
        var desktopCases = new (Exception Exception, int HResult)[]
        {
            (new Exception(), unchecked((int)0x80131500)),
            (new SystemException(), unchecked((int)0x80131501)),
            (new ArgumentException(), unchecked((int)0x80070057)),
            (new ArgumentNullException(), unchecked((int)0x80004003)),
            (new ArgumentOutOfRangeException(), unchecked((int)0x80131502)),
            (new ArithmeticException(), unchecked((int)0x80070216)),
            (new ArrayTypeMismatchException(), unchecked((int)0x80131503)),
            (new DivideByZeroException(), unchecked((int)0x80020012)),
            (new FormatException(), unchecked((int)0x80131537)),
            (new IndexOutOfRangeException(), unchecked((int)0x80131508)),
            (new InvalidCastException(), unchecked((int)0x80004002)),
            (new InvalidOperationException(), unchecked((int)0x80131509)),
            (new KeyNotFoundException(), unchecked((int)0x80131577)),
            (new NotSupportedException(), unchecked((int)0x80131515)),
            (new NullReferenceException(), unchecked((int)0x80004003)),
            (new ObjectDisposedException("object"), unchecked((int)0x80131622)),
            (new OperationCanceledException(), unchecked((int)0x8013153B)),
            (new OutOfMemoryException(), unchecked((int)0x8007000E)),
            (new OverflowException(), unchecked((int)0x80131516)),
            (new PlatformNotSupportedException(), unchecked((int)0x80131539)),
        };
#pragma warning restore CA2201
        Assert.All(desktopCases, item => Assert.Equal(item.HResult, item.Exception.HResult));

        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ExceptionHResultContract",
            """
            using System;
            using System.Collections.Generic;

            public static class Program
            {
                public static int Main(int value) =>
                    new Exception().HResult == unchecked((int)0x80131500) &&
                    new SystemException().HResult == unchecked((int)0x80131501) &&
                    new ArgumentException().HResult == unchecked((int)0x80070057) &&
                    new ArgumentNullException().HResult == unchecked((int)0x80004003) &&
                    new ArgumentOutOfRangeException().HResult == unchecked((int)0x80131502) &&
                    new ArithmeticException().HResult == unchecked((int)0x80070216) &&
                    new ArrayTypeMismatchException().HResult == unchecked((int)0x80131503) &&
                    new DivideByZeroException().HResult == unchecked((int)0x80020012) &&
                    new FormatException().HResult == unchecked((int)0x80131537) &&
                    new IndexOutOfRangeException().HResult == unchecked((int)0x80131508) &&
                    new InvalidCastException().HResult == unchecked((int)0x80004002) &&
                    new InvalidOperationException().HResult == unchecked((int)0x80131509) &&
                    new KeyNotFoundException().HResult == unchecked((int)0x80131577) &&
                    new NotSupportedException().HResult == unchecked((int)0x80131515) &&
                    new NullReferenceException().HResult == unchecked((int)0x80004003) &&
                    new ObjectDisposedException("object").HResult == unchecked((int)0x80131622) &&
                    new OperationCanceledException().HResult == unchecked((int)0x8013153B) &&
                    new OutOfMemoryException().HResult == unchecked((int)0x8007000E) &&
                    new OverflowException().HResult == unchecked((int)0x80131516) &&
                    new PlatformNotSupportedException().HResult == unchecked((int)0x80131539)
                        ? 1
                        : 0;
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.Library, assets.CoreLib],
            "Program",
            "Main",
            []));

        Assert.Equal(1, ExecuteWithNode(result.ApplicationModule, assets.Directory, 0));
    }

    [Fact]
    public void ReachableMapIncludesGenericDelegateAndAsyncExceptionPaths()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ExceptionReachabilityMechanisms",
            """
            using System;
            using System.Threading.Tasks;

            public sealed class GenericFailure<T> : Exception
            {
                public GenericFailure(string message) : base(message) { }
            }

            public sealed class AsyncFailure : Exception
            {
                public AsyncFailure(string message) : base(message) { }
            }

            public delegate Exception FailureFactory();

            public static class Program
            {
                private static Exception Create<T>() => new GenericFailure<T>("generic");

                private static async Task<int> FailAsync()
                {
                    await Task.Yield();
                    throw new AsyncFailure("async");
                }

                public static int Main(int value)
                {
                    if (value == 0)
                    {
                        FailureFactory factory = Create<int>;
                        throw factory();
                    }

                    return FailAsync().GetAwaiter().GetResult();
                }
            }
            """);

        var result = NetWasmCompiler.Compile(CompilerTestSupport.CreateReactorOptions(
            assets,
            assembly,
            "Program",
            "Main") with
        {
            ReferencePaths = [assets.Library, assets.CoreLib],
        });
        var artifacts = Assert.IsType<DiagnosticArtifactBundle>(result.DiagnosticArtifacts);
        using var map = JsonDocument.Parse(artifacts.ExceptionTypeMap.Bytes);
        var names = map.RootElement.GetProperty("entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("displayName").GetString())
            .ToArray();

        Assert.Contains(names, name => name?.Contains("GenericFailure", StringComparison.Ordinal) == true);
        Assert.Contains(names, name => name?.Contains("AsyncFailure", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ReachableMapIncludesExceptionsRaisedOnlyByCilSemantics()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ImplicitExceptionReachability",
            """
            public static class Program
            {
                public static int Main(int value) => value / (value - value);
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "Program",
            "Main",
            []));
        var artifacts = Assert.IsType<DiagnosticArtifactBundle>(result.DiagnosticArtifacts);
        using var map = JsonDocument.Parse(artifacts.ExceptionTypeMap.Bytes);

        Assert.Contains(
            map.RootElement.GetProperty("entries").EnumerateArray(),
            entry => entry.GetProperty("displayName").GetString() ==
                "System.DivideByZeroException");
    }

    [Fact]
    public void TerminalContractCompilesForDebugAndOptimizedCilAcrossTargetWidths()
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;

            public static class Program
            {
                public static int Main(int value)
                {
                    throw new InvalidOperationException("terminal matrix");
                }
            }
            """;
        var assemblies = new[]
        {
            assets.CompileSource("TerminalDebug", source),
            assets.CompileOptimizedSource("TerminalOptimized", source),
        };

        foreach (var assembly in assemblies)
            foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
            {
                var result = NetWasmCompiler.Compile(new CompilerOptions(
                    assembly,
                    [assets.Library, assets.CoreLib],
                    "Program",
                    "Main",
                    [],
                    target));

                ValidateWithNode(result.ApplicationModule, assets.Directory);
                var artifacts = Assert.IsType<DiagnosticArtifactBundle>(result.DiagnosticArtifacts);
                Assert.Equal(result.ApplicationModule, artifacts.Wasm);
                Assert.Equal("application.netwasm.exceptions.json", artifacts.ExceptionTypeMap.FileNameHint);
            }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnhandledExceptionWithoutDiagnosticsSaysHowToNameItBeforeTrapping(bool structuredDiagnostics)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("UnhandledHint", """
            using System;

            namespace UnhandledHint;

            public static class EntryPoint
            {
                public static int Run(int input) =>
                    input < 0 ? throw new InvalidOperationException("unnamed") : input + 1;
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "UnhandledHint.EntryPoint",
            "Run",
            [],
            StructuredDiagnostics: structuredDiagnostics));

        Assert.Equal(42, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 41));
        var terminal = Assert.ThrowsAny<Exception>(() =>
            ExecuteWithNode(compilation.ApplicationModule, assets.Directory, -1));
        Assert.Equal(!structuredDiagnostics,
            terminal.Message.Contains("unhandled exception hint", StringComparison.Ordinal));
        Assert.Equal(structuredDiagnostics,
            terminal.Message.Contains("unnamed", StringComparison.Ordinal));
        Assert.Contains("terminal wasm trap=true", terminal.Message, StringComparison.Ordinal);
    }

    // Only an entry point or an interop boundary reports or raises a terminal
    // exception. A component that exports through its world, as a WIT worker
    // does, imports neither from the host even with structured diagnostics: its
    // world provides no netwasm.host.v1, and at O0 no Binaryen pass removes an
    // unused import before the component is encoded.
    [Fact]
    public void AComponentWithoutATerminalBoundaryImportsNoTerminalExceptionHost()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ReactorTerminal", """
            using System;

            namespace ReactorTerminal;

            public static class EntryPoint
            {
                public static int Run(int input) =>
                    input < 0 ? throw new InvalidOperationException("unnamed") : input + 1;
            }
            """);

        var compilation = NetWasmCompiler.Compile(CreateReactorOptions(
            assets, assembly, "ReactorTerminal.EntryPoint", "Run") with
        { StructuredDiagnostics = true });

        Assert.DoesNotContain(compilation.FunctionImports, import => import.Module == RuntimeAbi.HostModule);
    }

    [Fact]
    public void ExceptionObjectContractIsStacklessCatchableAndAutomaticallyMapped()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ExceptionObjectFixture",
            """
            namespace ExceptionObjectFixture;

            public sealed class CustomException : System.Exception
            {
                public CustomException(string? message, System.Exception? inner)
                    : base(message, inner) { }

                public void SetCode(int value) => HResult = value;
            }

            public sealed class UnusedException : System.Exception
            {
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (input == -1)
                    {
                        throw new CustomException("borrowed terminal message", null);
                    }
                    if (input == -2)
                    {
                        throw new System.FormatException();
                    }
                    int result = 0;
                    var empty = new System.Exception();
                    if (empty.Message == "An exception was thrown.") result |= 1;
                    if (empty.StackTrace == null) result |= 2;

                    var inner = new System.Exception("inner");
                    var outer = new CustomException("outer", inner);
                    if (object.ReferenceEquals(outer.InnerException, inner)) result |= 4;
                    outer.Source = "fixture";
                    if (outer.Source == "fixture") result |= 8;
                    outer.SetCode(input);
                    if (outer.HResult == input) result |= 16;
                    if (outer.ToString() == "outer ---> inner") result |= 32;

                    int cleanup = 0;
                    try
                    {
                        try { throw outer; }
                        catch (CustomException) { throw; }
                        finally { cleanup++; }
                    }
                    catch (CustomException caught)
                    {
                        if (object.ReferenceEquals(caught, outer) && cleanup == 1) result |= 64;
                    }
                    return result;
                }
            }
            """);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExceptionObjectFixture.EntryPoint",
            "Run",
            [],
            StructuredDiagnostics: true));

        Assert.Equal(127, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 41));
        var terminal = Assert.ThrowsAny<Exception>(() =>
            CompilerTestSupport.ExecuteWithNode(compilation.ApplicationModule, assets.Directory, -1));
        Assert.Contains("terminal exception type=", terminal.Message, StringComparison.Ordinal);
        Assert.Contains("borrowed terminal message", terminal.Message, StringComparison.Ordinal);
        Assert.Contains("terminal wasm trap=true", terminal.Message, StringComparison.Ordinal);
        var parameterlessTerminal = Assert.ThrowsAny<Exception>(() =>
            CompilerTestSupport.ExecuteWithNode(
                compilation.ApplicationModule,
                assets.Directory,
                -2));
        // The diagnostic transport preserves the explicitly stored message.
        // Exception.Message still supplies its managed fallback, verified by
        // the successful-path assertion above, while an absent payload stays
        // distinguishable from an explicitly supplied message.
        Assert.Contains("message=null", parameterlessTerminal.Message,
            StringComparison.Ordinal);

        var exportCompilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExceptionObjectFixture.EntryPoint",
            "Run",
            [new RequestedExport("invoke", "ExceptionObjectFixture.EntryPoint", "Run")],
            StructuredDiagnostics: true));
        var exportTerminal = Assert.ThrowsAny<Exception>(() =>
            CompilerTestSupport.ExecuteWithNode(
                exportCompilation.ApplicationModule,
                assets.Directory,
                -1,
                exportName: "invoke"));
        Assert.Contains("terminal exception type=", exportTerminal.Message, StringComparison.Ordinal);
        Assert.Contains("borrowed terminal message", exportTerminal.Message, StringComparison.Ordinal);
        var artifacts = Assert.IsType<DiagnosticArtifactBundle>(compilation.DiagnosticArtifacts);
        using var map = JsonDocument.Parse(artifacts.ExceptionTypeMap.Bytes);
        Assert.Contains(
            map.RootElement.GetProperty("entries").EnumerateArray(),
            entry => entry.GetProperty("displayName").GetString() ==
                "ExceptionObjectFixture.CustomException");
        Assert.DoesNotContain(
                    map.RootElement.GetProperty("entries").EnumerateArray(),
                    entry => entry.GetProperty("canonicalIdentity").GetString()!.Contains(
                        "ExceptionObjectFixture.UnusedException",
                        StringComparison.Ordinal));
        var entries = map.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        var customEntry = Assert.Single(entries, entry =>
            entry.GetProperty("displayName").GetString() == "ExceptionObjectFixture.CustomException");
        var customTypeId = customEntry.GetProperty("typeId").GetInt32();
        Assert.Contains($"terminal exception type={customTypeId}", terminal.Message, StringComparison.Ordinal);

        var omittedCompilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ExceptionObjectFixture.EntryPoint",
            "Run",
            [],
            EmitExceptionTypeMap: false,
            StructuredDiagnostics: true));
        Assert.Null(omittedCompilation.DiagnosticArtifacts);
        var omittedTerminal = Assert.ThrowsAny<Exception>(() =>
            ExecuteWithNode(omittedCompilation.ApplicationModule, assets.Directory, -1));
        Assert.Contains("terminal exception type=", omittedTerminal.Message, StringComparison.Ordinal);
        Assert.Contains("borrowed terminal message", omittedTerminal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalUnwindPreservesRethrowFilterAndCleanupReplacementSemantics()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "TerminalUnwindFixture",
            """
            using System;

            public static class EntryPoint
            {
                public static int Run(int input) => input;

                public static int FailRethrow(int input)
                {
                    try { throw new InvalidOperationException("rethrow-original"); }
                    catch (InvalidOperationException) { throw; }
                }

                public static int FailFilter(int input)
                {
                    try { throw new InvalidOperationException("filter-original"); }
                    catch (Exception) when (ThrowingFilter()) { return input; }
                }

                public static int FailReplacement(int input)
                {
                    try { throw new InvalidOperationException("cleanup-original"); }
                    finally { throw new FormatException("cleanup-replacement"); }
                }

                private static bool ThrowingFilter() =>
                    throw new FormatException("filter-failure");
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [
                new RequestedExport("fail_rethrow", "EntryPoint", "FailRethrow"),
                new RequestedExport("fail_filter", "EntryPoint", "FailFilter"),
                new RequestedExport("fail_replacement", "EntryPoint", "FailReplacement"),
            ],
            StructuredDiagnostics: true));

        AssertTerminalMessage("fail_rethrow", "rethrow-original");
        AssertTerminalMessage("fail_filter", "filter-original");
        AssertTerminalMessage("fail_replacement", "cleanup-replacement");

        void AssertTerminalMessage(string exportName, string expected)
        {
            var terminal = Assert.ThrowsAny<Exception>(() => ExecuteWithNode(
                compilation.ApplicationModule,
                assets.Directory,
                0,
                exportName: exportName));
            Assert.Contains(expected, terminal.Message, StringComparison.Ordinal);
        }
    }
}
