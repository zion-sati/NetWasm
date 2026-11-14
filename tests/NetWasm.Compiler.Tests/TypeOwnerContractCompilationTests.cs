using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TypeOwnerContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void TypeOwnerAndAssemblyComeFromTheTypeTable(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("TypeOwnerFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "TypeOwnerFixture.EntryPoint",
            "Run", [new("run", "TypeOwnerFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        namespace TypeOwnerFixture;
        public static class EntryPoint
        {
         public sealed class Inner { }

         public static int Run(int input)
         {
          var type = typeof(EntryPoint);
          var caught = 0;
          try { var value = type.GUID; } catch (PlatformNotSupportedException) { caught++; }
          try { var value = type.StructLayoutAttribute; } catch (PlatformNotSupportedException) { caught++; }
          try { var value = Type.DefaultBinder; } catch (PlatformNotSupportedException) { caught++; }
          // The runtime type table answers a type's owner: its declaring type, and so whether it is nested.
          var inner = typeof(Inner);
          if (type.DeclaringType is not null || type.ReflectedType is not null || type.IsNested) return 1;
          if (inner.DeclaringType != type || inner.ReflectedType != type || !inner.IsNested) return 4;
          if (!type.IsClass || type != Type.GetTypeFromHandle(type.TypeHandle)) return 2;
          // The assembly-qualified name answers with the stand-in for the assembly a program does not carry.
          if (type.AssemblyQualifiedName is null) return 5;
          // The type table answers each type's assembly too: one object for the types of one assembly.
          if (type.Assembly is null || type.Assembly != inner.Assembly || type.Assembly == typeof(int).Assembly) return 6;
          // So do the module, the member kind and the attribute-flag queries, with desktop's answers.
          if (type.Module is null || type.Module != inner.Module || type.Module != type.Assembly.ManifestModule) return 7;
          if (type.MemberType != System.Reflection.MemberTypes.TypeInfo ||
              inner.MemberType != System.Reflection.MemberTypes.NestedType) return 8;
          if (type.IsSerializable || !type.IsVisible || !inner.IsVisible) return 9;
          return caught == 3 ? input + 1 : 3;
         }
        }
        """;
}
