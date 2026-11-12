using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static NetWasm.Compiler.Tests.CompilerTestSupport;

// Siblings of issue 89's JSImport method groups: a method the host supplies can be named by a
// function pointer as well as a delegate, a [WasmImportLinkage] import by either, one that takes
// and returns structs (which the native ABI passes through memory) by a delegate, and a delegate
// bound to an import converts to another delegate type. Each compiles to valid Wasm that calls
// the one import, or is refused as a delegate bound to the same import is.
public sealed class ImportMethodGroupCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void FunctionPointersAndConvertedDelegatesNameAJSImport(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource(
            "JSImportFunctionPointerFixture",
            """
            using System;
            using System.Runtime.InteropServices.JavaScript;

            public static unsafe class Program
            {
                public static int Run(int input)
                {
                    delegate*<string, string?> resolve = &Host.Resolve;
                    delegate*<int, int> twice = &Host.Twice;
                    Func<int, int> bound = Host.Twice;
                    Converter<int, int> converted = new(bound);
                    Func<int, int> invoked = bound.Invoke;
                    return (resolve("x")?.Length ?? 0) + twice(input) + converted(input) + invoked(input);
                }
            }

            internal static class Host
            {
                [JSImport("resolve", "repro.host")]
                internal static extern string? Resolve(string name);

                [JSImport("twice", "repro.host")]
                internal static extern int Twice(int value);
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "Program",
            "Run",
            [],
            target));

        ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(["resolve", "twice"],
            result.InteropManifest.Imports.Select(import => import.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DelegatesAndFunctionPointersNameAWasmImportLinkageImport(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource(
            "WasmImportLinkageMethodGroupFixture",
            """
            using System;
            using System.Runtime.InteropServices;

            public static class HostUi
            {
                [DllImport("host_ui", EntryPoint = "create_node"), WasmImportLinkage]
                public static extern ulong CreateNode(uint type);
            }

            public static unsafe class EntryPoint
            {
                public static int Run(int input)
                {
                    Func<uint, ulong> create = HostUi.CreateNode;
                    delegate*<uint, ulong> pointer = &HostUi.CreateNode;
                    return (int)(create((uint)input) + pointer((uint)input + 1));
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            target));

        ValidateWithNode(result.ApplicationModule, assets.Directory);
        var import = Assert.Single(result.FunctionImports, candidate => candidate.Name == "create_node");
        Assert.Equal("host_ui", import.Module);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DelegatesNameAnImportThatTakesAndReturnsStructs(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource(
            "NativeAggregateMethodGroupFixture",
            """
            using System;
            using System.Runtime.InteropServices;

            public struct Size { public double Width; public double Height; public int Flags; }

            public static class HostUi
            {
                [DllImport("host_ui", EntryPoint = "measure"), WasmImportLinkage]
                public static extern Size Measure(Size available, int node);
            }

            public static unsafe class EntryPoint
            {
                public static int Run(int input)
                {
                    var available = new Size { Width = input, Height = 2, Flags = 1 };
                    Func<Size, int, Size> measure = HostUi.Measure;
                    Func<Size, int, Size> converted = new(measure);
                    return (int)(measure(available, input).Width + converted(available, input + 1).Height);
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            target));

        ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Single(result.FunctionImports, candidate => candidate.Name == "measure");
    }

    [Fact]
    public void AFunctionPointerToAnAsynchronousJSImportIsRefusedAsADelegateIs()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource(
            "JSImportAsyncFunctionPointerFixture",
            """
            using System.Runtime.InteropServices.JavaScript;
            using System.Threading.Tasks;

            public static unsafe class Program
            {
                public static int Run(int input)
                {
                    delegate*<Task<int>> fetch = &Host.FetchAsync;
                    return fetch().IsCompleted ? 1 : 0;
                }
            }

            internal static class Host
            {
                [JSImport("fetch", "repro.host")]
                internal static extern Task<int> FetchAsync();
            }
            """);

        var exception = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "Program",
            "Run",
            [])));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Contains("returns a Task or ValueTask", exception.Diagnostic.Message, StringComparison.Ordinal);
    }
}
