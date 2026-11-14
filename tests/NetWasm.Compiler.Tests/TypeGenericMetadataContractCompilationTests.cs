using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TypeGenericMetadataContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void GenericMetadataAnswersFromTheTypeTable(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("TypeGenericMetadataFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "TypeGenericMetadataFixture.EntryPoint",
            "Run", [new("run", "TypeGenericMetadataFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        namespace TypeGenericMetadataFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var parameter = typeof(Box<>).GetGenericArguments()[0];
                if (parameter.DeclaringMethod != null || typeof(Box<>).GetGenericTypeDefinition() != typeof(Box<>)) return 1;
                var caught = 0;
                foreach (var type in new Type[] { typeof(int), typeof(Box<>), typeof(Box<int>), typeof(int[]) })
                {
                    try { var value = type.GenericParameterPosition; } catch (InvalidOperationException) { caught++; }
                    try { var value = type.GenericParameterAttributes; } catch (InvalidOperationException) { caught++; }
                    try { var value = type.DeclaringMethod; } catch (InvalidOperationException) { caught++; }
                    try { type.GetGenericParameterConstraints(); } catch (InvalidOperationException) { caught++; }
                }
                try { typeof(int).GetGenericTypeDefinition(); } catch (InvalidOperationException) { caught++; }
                // The type table answers a generic parameter's position and attributes and a constructed type's
                // definition; constraints stay outside the profile.
                if (parameter.GenericParameterPosition != 0 ||
                    parameter.GenericParameterAttributes != System.Reflection.GenericParameterAttributes.None ||
                    typeof(Box<int>).GetGenericTypeDefinition() != typeof(Box<>)) return 3;
                try { parameter.GetGenericParameterConstraints(); } catch (PlatformNotSupportedException) { caught++; }
                return caught == 18 ? input + 1 : 2;
            }
            private class Box<T> { }
        }
        """;
}
