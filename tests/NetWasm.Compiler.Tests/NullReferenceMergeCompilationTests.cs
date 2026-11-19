// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// `ref s is null ? ref *(char*)0 : ref s.GetPinnableReference()` (Utf16StringMarshaller.GetPinnableReference) joins a
// native int and a managed pointer at one stack depth. Desktop .NET's importer takes the pair as a managed pointer;
// NetWasm rejected the merge (NW1002) once O3's specialization kept both arms.
public sealed class NullReferenceMergeCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.InteropServices.Marshalling;

        namespace NullReferenceMergeFixture;

        public static unsafe class EntryPoint
        {
            private static ref readonly char Pin(string? text) =>
                ref text is null ? ref *(char*)0 : ref text.GetPinnableReference();

            private static int Read(string? text)
            {
                fixed (char* pointer = &Pin(text))
                {
                    return pointer == null ? -1 : pointer[1];
                }
            }

            private static int ReadMarshalled(string? text)
            {
                fixed (char* pointer = &Utf16StringMarshaller.GetPinnableReference(text))
                {
                    return pointer == null ? -1 : pointer[0];
                }
            }

            public static int Run(int input)
            {
                if (Read(null) != -1 || Read("abc") != 'b') return 1;
                if (ReadMarshalled(null) != -1 || ReadMarshalled("xyz") != 'x') return 2;
                if (ReadMarshalled(input > 0 ? "q" : null) != 'q') return 3;
                return input + 1;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O2)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void NullPointerAndReferenceArmsMergeAsAManagedPointer(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedUnsafeSource("NullReferenceMergeFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "NullReferenceMergeFixture.EntryPoint", "Run",
            [], Target: target, Optimization: optimization));
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }
}
