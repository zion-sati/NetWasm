using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// IntPtr.Size and the compiled host are constants before reachability, as the JIT folds them, so
// the branch a target or host never takes is removed with its callees and data rather than kept
// until Wasm emission.
public sealed class TargetConstantBranchCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void OnlyTheTargetsBranchIsReachable(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            namespace TargetConstantFixture;
            public static class EntryPoint
            {
                [MethodImpl(MethodImplOptions.NoInlining)] static int OnlyWide() => 8;
                [MethodImpl(MethodImplOptions.NoInlining)] static int OnlyNarrow() => 4;
                public static unsafe int Run(int input)
                {
                    int size = IntPtr.Size == 8 ? OnlyWide() : OnlyNarrow();
                    if (UIntPtr.Size != size || sizeof(nint) != size || sizeof(void*) != size || Unsafe.SizeOf<nuint>() != size) return -1;
                    if (!OperatingSystem.IsBrowser() || OperatingSystem.IsWasi() || RuntimeInformation.OSDescription != "Browser") return -2;
                    return size * 10 + input;
                }
            }
            """;
        var assembly = optimize
            ? assets.CompileOptimizedUnsafeSource("TargetConstantFixture", source)
            : assets.CompileUnsafeSource("TargetConstantFixture", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "TargetConstantFixture.EntryPoint", "Run", [], Target: target));
        var methods = result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values)
            .Select(body => body.Method).ToArray();

        var wide = target == WasmTarget.Wasm64;
        Assert.Equal(wide, methods.Any(method => method.Definition.Name == "OnlyWide"));
        Assert.Equal(!wide, methods.Any(method => method.Definition.Name == "OnlyNarrow"));
        Assert.DoesNotContain(methods, method => method.Definition.Name == "get_Size" &&
            method.DeclaringType.CanonicalName is "primitive:nativeint" or "primitive:nativeuint");
        Assert.DoesNotContain(methods, method => method.Definition.Name == "get_IsJavaScriptHost");
        Assert.Equal(wide ? 81 : 41, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 1, target, result.StaticDataEnd));
    }
}
