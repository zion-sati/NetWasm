using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TypeAttributeContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void TypeAttributesAnswerAsDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("TypeAttributeFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "TypeAttributeFixture.EntryPoint",
            "Run", [new("run", "TypeAttributeFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        namespace TypeAttributeFixture;
        public static class EntryPoint
        {
         public static int Run(int input)
         {
          var type = typeof(EntryPoint);
          // The type table answers the attribute flags and the queries built on them, with desktop's values for a
          // public static class.
          if (type.Attributes != (System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Abstract |
              System.Reflection.TypeAttributes.Sealed | System.Reflection.TypeAttributes.BeforeFieldInit)) return 2;
          if (type.IsImport || type.IsSpecialName || type.IsNestedAssembly || type.IsNestedFamANDAssem || type.IsNestedFamily ||
              type.IsNestedFamORAssem || type.IsNestedPrivate || type.IsNestedPublic || type.IsNotPublic || !type.IsPublic) return 3;
          if (!type.IsAutoLayout || type.IsExplicitLayout || type.IsLayoutSequential) return 4;
          if (!type.IsAnsiClass || type.IsAutoClass || type.IsUnicodeClass) return 5;
          if (!type.IsClass || type.IsValueType || type.IsArray || !type.IsAbstract || !type.IsSealed) return 1;
          return input + 1;
         }
        }
        """;
}
