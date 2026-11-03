using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A program without a JavaScript host, such as a WASI build of a library whose
// browser path checks OperatingSystem.IsBrowser(), can reach JSObject.Dispose
// and JSSubscription.Dispose. No JavaScript import gave an object a handle, so
// each compiles to its null check, and a null receiver still throws.
public sealed class JavaScriptDisposeWithoutHostCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices.JavaScript;

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static JSObject? Object(int input) => null;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static JSSubscription? Subscription(int input) => null;

            public static int Run(int input)
            {
                var result = input;
                try { Object(input)!.Dispose(); } catch (NullReferenceException) { result += 100; }
                try { Subscription(input)!.Dispose(); } catch (NullReferenceException) { result += 1000; }
                IDisposable? disposable = input > 50 ? Object(input) : Subscription(input);
                disposable?.Dispose();
                return result;
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void DisposeWithoutAJavaScriptHostKeepsItsNullCheck(FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("JavaScriptDisposeWithoutHost" + optimization, Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: optimization));
        Assert.Equal(1107, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 7, WasmTarget.Wasm32, result.StaticDataEnd));
    }
}
