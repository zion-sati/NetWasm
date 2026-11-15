// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ConstrainedNullReceiverCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void GenericVirtualCallsRejectNullEvenWhenTheSealedOverrideDoesNotReadThis(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            public sealed class ConstantText { public override string ToString() => "constant"; }
            public struct ValueText { public override string ToString() => "value"; }
            public static class EntryPoint
            {
                private static string Format<T>(T value) => value!.ToString()!;
                public static int Run(int input)
                {
                    var rejected = 0;
                    try { Format<string>(null!); } catch (NullReferenceException) { rejected++; }
                    try { Format<ConstantText>(null!); } catch (NullReferenceException) { rejected++; }
                    return rejected == 2 && Format("text") == "text" &&
                        Format(new ConstantText()) == "constant" && Format(new ValueText()) == "value" ? input : -1;
                }
            }
            """;
        var assembly = optimize ? assets.CompileOptimizedSource("ConstrainedNull", source)
            : assets.CompileSource("ConstrainedNull", source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 42, target, result.StaticDataEnd));
    }
}
