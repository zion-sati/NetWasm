using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// .NET code binds some native functions from more than one P/Invoke (overloads over a pointer and over IntPtr, say, as
// System.Security.Cryptography's do). They share one import; declarations that disagree on the signature do not
// compile. The harness links no native library, so the test reads the emitted module's imports.
public sealed class NativeImportSharingCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void OverloadsOfOneEntryPointShareItsImport(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource("NativeImportSharingFixture", Source);

        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "NativeImportSharingFixture.EntryPoint",
            "Run", [new("run", "NativeImportSharingFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));

        var imports = WasmModuleInspection.ReadFunctionImports(result.ApplicationModule);
        Assert.Single(imports, import => import.Name == "sample_write");
        Assert.Single(imports, import => import.Name == "sample_close");
    }

    [Fact]
    public void DeclarationsThatDisagreeOnTheSignatureDoNotCompile()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource("NativeImportSharingFixture", Source.Replace(
            "// conflicting declaration",
            "[DllImport(\"libsample\", EntryPoint = \"sample_write\")] private static extern long WriteWide(IntPtr data, int length);",
            StringComparison.Ordinal).Replace("// conflicting call", "_ = WriteWide(IntPtr.Zero, 0);", StringComparison.Ordinal));

        var error = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(new(assembly, [assets.CoreLib],
            "NativeImportSharingFixture.EntryPoint", "Run", [new("run", "NativeImportSharingFixture.EntryPoint", "Run")],
            Target: WasmTarget.Wasm32, WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0")));

        Assert.Equal(DiagnosticCode.NativeInterop, error.Diagnostic.Code);
    }

    private const string Source = """
        using System;
        using System.Runtime.InteropServices;
        namespace NativeImportSharingFixture;

        public static unsafe class EntryPoint
        {
            [DllImport("libsample", EntryPoint = "sample_write")]
            private static extern int WritePointer(byte* data, int length);

            [DllImport("libsample", EntryPoint = "sample_write")]
            private static extern int WriteHandle(IntPtr data, int length);

            [DllImport("libsample", EntryPoint = "sample_close")]
            private static extern void Close(IntPtr handle);

            // conflicting declaration

            public static int Run(int input)
            {
                if (input != 12345)
                {
                    return input + 1;
                }

                byte value = 1;
                int written = WritePointer(&value, 1) + WriteHandle((IntPtr)(&value), 1);
                Close(IntPtr.Zero);
                // conflicting call
                return written;
            }
        }
        """;
}
