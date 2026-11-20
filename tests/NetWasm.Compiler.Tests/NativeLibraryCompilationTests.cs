// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// NetWasm links native code into the program when it is built, so NativeLibrary answers every load as .NET does for a
// library that is not there and every export lookup as for a symbol that is not there, with .NET's argument checks; a
// DllImportResolver is recorded and refused a second time for the same assembly, as .NET refuses it. The native mule
// in NetWasm.Runtime.Pack.Tests checks the part desktop .NET answers the same way, with a resolver registered before
// P/Invokes into a linked archive.
public sealed class NativeLibraryCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void NativeLibraryAnswersAsForALibraryThatIsNotThere(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("NativeLibraryAnswers", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target));

        Assert.Equal(1023, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 0, target,
            result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Reflection;
        using System.Runtime.InteropServices;

        public static class EntryPoint
        {
            private static int Throws<TException>(Action action, int bit) where TException : Exception
            {
                try
                {
                    action();
                    return 0;
                }
                catch (Exception exception)
                {
                    return exception.GetType() == typeof(TException) ? bit : 0;
                }
            }

            public static int Run(int input)
            {
                var assembly = typeof(EntryPoint).Assembly;
                var score = 0;
                score += Throws<DllNotFoundException>(() => NativeLibrary.Load("sqlite3", assembly, null), 1);
                score += !NativeLibrary.TryLoad("sqlite3", assembly, DllImportSearchPath.SafeDirectories, out var handle) &&
                    handle == IntPtr.Zero ? 2 : 0;
                score += Throws<EntryPointNotFoundException>(() => NativeLibrary.GetExport(new IntPtr(1), "sqlite3_open"), 4);
                score += !NativeLibrary.TryGetExport(new IntPtr(1), "sqlite3_open", out var address) && address == IntPtr.Zero
                    ? 8 : 0;
                score += Throws<PlatformNotSupportedException>(() => NativeLibrary.GetMainProgramHandle(), 16);
                NativeLibrary.Free(IntPtr.Zero);
                NativeLibrary.Free(new IntPtr(1));
                score += Throws<ArgumentNullException>(() => NativeLibrary.Load(null!), 32);
                score += Throws<ArgumentNullException>(() => NativeLibrary.GetExport(new IntPtr(1), null!), 64);
                DllImportResolver resolver = static (name, from, path) => IntPtr.Zero;
                NativeLibrary.SetDllImportResolver(assembly, resolver);
                score += Throws<InvalidOperationException>(() => NativeLibrary.SetDllImportResolver(assembly, resolver), 128);
                score += Throws<ArgumentNullException>(() => NativeLibrary.SetDllImportResolver(assembly, null!), 256);
                try
                {
                    NativeLibrary.Load("libskia");
                }
                catch (DllNotFoundException exception) when (exception.Message.Contains("'libskia'", StringComparison.Ordinal))
                {
                    score += 512;
                }
                return score + input;
            }
        }
        """;
}
