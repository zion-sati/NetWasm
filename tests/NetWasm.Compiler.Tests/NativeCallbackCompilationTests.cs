using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class NativeCallbackCompilationTests
{
    [Fact]
    public void CompilerUnifiesNamedAndAddressTakenCallbackReachability()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource(
            "NamedAddressedCallbackFixture",
            """
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            namespace NamedAddressedCallbackFixture;

            public static unsafe class EntryPoint
            {
                [UnmanagedCallersOnly(
                    EntryPoint = "named_callback",
                    CallConvs = [typeof(CallConvCdecl)])]
                private static int Callback(int value) => value + 1;

                public static int Run(int value)
                {
                    delegate* unmanaged[Cdecl]<int, int> callback = &Callback;
                    return callback == null ? 0 : value;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "NamedAddressedCallbackFixture.EntryPoint",
            "Run",
            [],
            Target: WasmTarget.Wasm32));

        var support = Assert.IsType<WasmNativeCallbackSupportArtifact>(
            result.NativeCallbackSupport);
        var callback = Assert.Single(support.Callbacks);
        Assert.Equal("named_callback", callback.NativeSymbol);
        Assert.Equal("named_callback", callback.ApplicationExportName);
        Assert.NotNull(callback.RuntimeGetterExportName);
    }

    // Issue 226's repro: a call through delegate* unmanaged to an [UnmanagedCallersOnly] method
    // compiles to the C ABI trampoline for (int)->int, beside the method's address getter. The
    // native integration test (NativeUnmanagedCallIntegration) runs it through the linked table.
    [Fact]
    public void CompilesACallThroughAnUnmanagedFunctionPointer()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource(
            "UnmanagedCallFixture",
            """
            using System.Runtime.InteropServices;

            namespace UnmanagedCallFixture;

            public static unsafe class EntryPoint
            {
                public static int Run(int value)
                {
                    delegate* unmanaged<int, int> twice = &Native.Twice;
                    return twice(value);
                }
            }

            public static class Native
            {
                [UnmanagedCallersOnly]
                public static int Twice(int value) => value * 2;
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "UnmanagedCallFixture.EntryPoint",
            "Run",
            [],
            Target: WasmTarget.Wasm32));

        var support = Assert.IsType<WasmNativeCallbackSupportArtifact>(result.NativeCallbackSupport);
        Assert.Equal("netwasm_calli_ii", Assert.Single(support.Trampolines));
        var getter = Assert.Single(support.Callbacks).RuntimeGetterExportName;
        Assert.Equal([getter!, "netwasm_calli_ii"], support.TemporaryRuntimeExports);
    }

    // Every convention desktop .NET 11 accepts is the C ABI: calls through each, to native code and
    // to callbacks declared with each, share the (int)->int trampoline.
    [Fact]
    public void CompilesACallThroughEachConventionDesktopAccepts()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource(
            "ConventionsFixture",
            """
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            namespace ConventionsFixture;

            public static unsafe class EntryPoint
            {
                public static int Run(int value)
                {
                    var native = (void*)(nint)value;
                    delegate* unmanaged[Stdcall]<int, int> stdcall = &Native.Stdcall;
                    delegate* unmanaged[Thiscall, MemberFunction]<int, int> thiscall = &Native.Thiscall;
                    return ((delegate* unmanaged[Stdcall]<int, int>)native)(value) +
                        ((delegate* unmanaged[Thiscall]<int, int>)native)(value) +
                        ((delegate* unmanaged[Cdecl, SuppressGCTransition]<int, int>)native)(value) +
                        ((delegate* unmanaged[Stdcall, MemberFunction]<int, int>)native)(value) +
                        stdcall(value) + thiscall(value);
                }
            }

            public static class Native
            {
                [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
                public static int Stdcall(int value) => value * 2;

                [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall), typeof(CallConvMemberFunction)])]
                public static int Thiscall(int value) => value * 3;
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ConventionsFixture.EntryPoint",
            "Run",
            [],
            Target: WasmTarget.Wasm32));

        var support = Assert.IsType<WasmNativeCallbackSupportArtifact>(result.NativeCallbackSupport);
        Assert.Equal("netwasm_calli_ii", Assert.Single(support.Trampolines));
        Assert.Equal(2, support.Callbacks.Length);
    }

    // A convention desktop .NET fails (Fastcall) is reported where the call is, which the issue's
    // error was not.
    [Fact]
    public void NamesTheMethodAndOffsetOfAnUnsupportedUnmanagedCall()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource(
            "FastcallFixture",
            """
            namespace FastcallFixture;

            public static unsafe class EntryPoint
            {
                public static int Run(int value) =>
                    value == 0 ? 0 : ((delegate* unmanaged[Fastcall]<int, int>)(nint)value)(value);
            }
            """);

        var error = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "FastcallFixture.EntryPoint",
            "Run",
            [],
            Target: WasmTarget.Wasm32)));

        Assert.Equal(DiagnosticCode.NativeInterop, error.Diagnostic.Code);
        Assert.Contains("EntryPoint::Run", error.Diagnostic.Method, StringComparison.Ordinal);
        Assert.NotNull(error.Diagnostic.IlOffset);
        Assert.Contains("calling convention 'FastCall' is not supported", error.Diagnostic.Message, StringComparison.Ordinal);
    }
}
