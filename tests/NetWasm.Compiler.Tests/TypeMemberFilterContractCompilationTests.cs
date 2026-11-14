using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TypeMemberFilterContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void MemberFilteringPreservesEmptySelectionAndRuntimeFallbacks(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("TypeMemberFilterFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "TypeMemberFilterFixture.EntryPoint",
            "Run", [new("run", "TypeMemberFilterFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Reflection;
        namespace TypeMemberFilterFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var calls = 0;
                MemberFilter filter = (member, criteria) => { calls++; return object.ReferenceEquals(member, criteria); };
                if (!filter(typeof(int), typeof(int))) return 1;
                calls = 0;
                var type = typeof(EntryPoint);
                if (type.FindMembers(0, BindingFlags.Public, filter, null).Length != 0 || calls != 0) return 2;
                if (type.FindMembers(0, BindingFlags.Public, null, null).Length != 0) return 3;
                var caught = 0;
                // Nested types come from the type table (EntryPoint has none); other members need member metadata.
                if (type.FindMembers(MemberTypes.NestedType, BindingFlags.Public | BindingFlags.Instance, filter, null).Length != 0 ||
                    type.FindMembers(MemberTypes.NestedType, BindingFlags.Public | BindingFlags.Static, null, null).Length != 0 || calls != 0) return 5;
                MemberTypes[] kinds = { MemberTypes.Method, MemberTypes.Constructor, MemberTypes.Field, MemberTypes.Property, MemberTypes.Event, MemberTypes.All };
                foreach (var kind in kinds)
                {
                    try { type.FindMembers(kind, BindingFlags.Public | BindingFlags.Instance, filter, null); }
                    catch (PlatformNotSupportedException) { caught++; }
                    try { type.FindMembers(kind, BindingFlags.Public | BindingFlags.Static, null, null); }
                    catch (PlatformNotSupportedException) { caught++; }
                }
                return caught == 12 && calls == 0 ? input + 1 : 4;
            }
        }
        """;
}
