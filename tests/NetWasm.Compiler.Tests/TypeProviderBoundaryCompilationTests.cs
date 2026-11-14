using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TypeProviderBoundaryCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CanonicalTypesRetainIdentityAndCustomConstructionThrows(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("TypeProviderFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "TypeProviderFixture.EntryPoint",
            "Run", [new("run", "TypeProviderFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Reflection;
        namespace TypeProviderFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var type = typeof(int);
                object value = input;
                if (!ReferenceEquals(type, value.GetType())) return 1;
                if (!ReferenceEquals(type, Type.GetTypeFromHandle(type.TypeHandle))) return 2;
                // A program carries no type names (issue 225), so the type is checked by identity above.
                if (!type.IsValueType || type.IsEnum) return 3;
                if (!typeof(object).IsAssignableFrom(type) || !type.IsInstanceOfType(value)) return 4;
                if (typeof(Derived).BaseType != typeof(Type)) return 5;
                try { new Derived(); return 6; }
                catch (PlatformNotSupportedException e) { if (e.Message.Length == 0) return 7; }
                // Every Type object is a RuntimeType, a TypeInfo, as on .NET 11: GetTypeInfo returns the type itself.
                if (!ReferenceEquals(typeof(int).GetTypeInfo(), typeof(int))) return 10;
                var caught = 0;
                try { new DerivedInfo(); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { _ = new EmptyAssembly().DefinedTypes; } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { IntrospectionExtensions.GetTypeInfo(null!); } catch (ArgumentNullException e) { if (e.ParamName == "type") caught++; }
                if (caught != 3) return 8;
                if (typeof(TypeInfo).BaseType != typeof(Type)) return 9;
                return input + 1;
            }
            private sealed class Derived : Type { }
            private sealed class DerivedInfo : TypeInfo { }
            private sealed class EmptyAssembly : Assembly { }
        }
        """;
}
