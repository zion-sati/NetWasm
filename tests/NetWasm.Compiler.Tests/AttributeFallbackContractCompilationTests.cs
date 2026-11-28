// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class AttributeFallbackContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void UnfilteredQueriesAnswerForKnownTypesAndFallBackForRunTimeOnes(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("AttributeFallbackFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "AttributeFallbackFixture.EntryPoint",
            "Run", [new("run", "AttributeFallbackFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Reflection;
        namespace AttributeFallbackFixture;
        public static class EntryPoint
        {
            // A known type's unfiltered queries answer as desktop's do (its attributes, as object[] or Attribute[]); one on a
            // Type known only at run time would take every attribute of every type the program has, so it keeps the
            // catchable fallback.
            public static int Run(int input)
            {
                if (typeof(Target).GetCustomAttributes(true) is not { Length: 0 } own || own.GetType() != typeof(object[])) return 1;
                if (CustomAttributeExtensions.GetCustomAttributes(typeof(Target)) is not Attribute[] { Length: 0 }) return 2;
                if (CustomAttributeExtensions.GetCustomAttributes(typeof(Target), false) is not Attribute[] { Length: 0 }) return 3;
                if (((ICustomAttributeProvider)typeof(Target)).GetCustomAttributes(false).Length != 0) return 4;
                var runtime = Pick(input);
                var caught = 0;
                try { runtime.GetCustomAttributes(true); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes(runtime, false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                return caught == 2 ? input + 1 : 5;
            }
            private static Type Pick(int input) => input > 0 ? typeof(Target) : typeof(EntryPoint);
            private sealed class Target { }
        }
        """;
}
