// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// RuntimeHelpers.AllocateTypeAssociatedMemory, upstream's NativeAOT and Mono bodies: zeroed native memory, aligned when
// asked, never freed since NetWasm unloads no type; and Marshal.GetExceptionPointers, which throws
// PlatformNotSupportedException there as on NativeAOT and Mono (CoreCLR returns Windows exception pointers, so the
// desktop build of the fixture leaves that check out). Both are .NET 5 and later members a net8.0 package build calls
// (SharpCompress, OpenTelemetry).
public sealed class TypeAssociatedMemoryCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void AllocatesZeroedAlignedMemoryAsDesktopDoes(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource("TypeAssociatedMemoryFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "TypeAssociatedMemoryFixture.EntryPoint",
            "Run", [new("run", "TypeAssociatedMemoryFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    internal const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;

        namespace TypeAssociatedMemoryFixture;

        public static unsafe class EntryPoint
        {
            private static bool Zeroed(IntPtr memory, int size)
            {
                var bytes = (byte*)memory;
                for (var i = 0; i < size; i++) if (bytes[i] != 0) return false;
                return true;
            }

            private static bool Throws<T>(Action action) where T : Exception
            {
                try { action(); return false; }
                catch (T) { return true; }
            }

            public static int Run(int input)
            {
                var memory = RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(EntryPoint), 64);
                if (memory == IntPtr.Zero || !Zeroed(memory, 64)) return 1;
                ((byte*)memory)[63] = 7;
                if (((byte*)memory)[63] != 7) return 2;
                var other = RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(string), 64);
                if (other == memory || !Zeroed(other, 64)) return 3;

                foreach (var alignment in new[] { 1, 8, 64, 256 })
                {
                    var aligned = RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(EntryPoint), 48, alignment);
                    if (aligned == IntPtr.Zero || (long)aligned % alignment != 0 || !Zeroed(aligned, 48)) return 10 + alignment % 7;
                }

                if (!Throws<ArgumentOutOfRangeException>(() => RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(EntryPoint), -1))) return 20;
                if (!Throws<ArgumentOutOfRangeException>(() => RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(EntryPoint), -1, 8))) return 21;
                if (!Throws<ArgumentException>(() => RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(EntryPoint), 8, 3))) return 22;
                if (!Throws<ArgumentException>(() => RuntimeHelpers.AllocateTypeAssociatedMemory(null!, 8))) return 23;
        #if !DESKTOP_ORACLE
                if (!Throws<PlatformNotSupportedException>(() => Marshal.GetExceptionPointers())) return 30;
        #endif
                return input + 1;
            }
        }
        """;
}
