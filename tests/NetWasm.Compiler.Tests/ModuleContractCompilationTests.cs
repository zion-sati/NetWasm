using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ModuleContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ModuleForwardingWorksAndDiscoveryRejects(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ModuleFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ModuleFixture.EntryPoint",
            "Run", [new("run", "ModuleFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Reflection;
        namespace ModuleFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (Portable(input) != 42) return 20;
                Module m = new EmptyModule(); Assembly a = new EmptyAssembly();
                var calls = 0; ModuleResolveEventHandler handler = (s,e) => { calls++; return m; };
                Action[] rejectedCalls = {
                    () => { _ = m.Assembly; }, () => { _ = m.FullyQualifiedName; }, () => { _ = m.Name; }, () => { _ = m.ScopeName; },
                    () => { _ = m.MDStreamVersion; }, () => { _ = m.ModuleVersionId; }, () => m.IsResource(), () => m.GetTypes(),
                    () => m.IsDefined(typeof(ObsoleteAttribute), false), () => m.GetCustomAttributes(false), () => m.GetCustomAttributes(typeof(ObsoleteAttribute), false),
                    () => m.GetObjectData(null!, default), () => m.ToString(),
                    () => { _ = a.ManifestModule; }, () => a.GetModule("X"), () => a.GetModules(), () => a.GetModules(true),
                    () => { _ = a.Modules; }, () => a.GetLoadedModules(), () => a.GetLoadedModules(true),
                    () => a.LoadModule("X", new byte[0]), () => a.LoadModule("X", new byte[0], null),
                    () => { a.ModuleResolve += handler; }, () => { a.ModuleResolve -= handler; }
                };
                var rejected = 0;
                foreach (var call in rejectedCalls) { try { call(); } catch (PlatformNotSupportedException) { rejected++; } }
                if (rejected != rejectedCalls.Length || calls != 0) return 21;
                return input + 1;
            }
            public static int Portable(int input)
            {
                var first = new CustomModule(new[] { typeof(int), typeof(string) });
                var second = new CustomModule(new[] { typeof(object) });
                Module alias = first;
                if (first != alias || first == second || first == null || null == first || !first.Equals(alias) || first.GetHashCode() != alias.GetHashCode()) return 1;
                if (first.ToString() != "scope") return 2;
                var owner = new CustomAssembly(Array.Empty<Module>());
                if (owner.GetTypes().Length != 0) return 3;
                owner.Items = new Module[] { first };
                if (!ReferenceEquals(owner.GetTypes(), first.Types)) return 4;
                owner.Items = new Module[] { first, second };
                var types = owner.GetTypes();
                if (types.Length != 3 || types[0] != typeof(int) || types[1] != typeof(string) || types[2] != typeof(object)) return 5;
                owner.GetModules(); if (owner.Resources) return 6;
                owner.GetModules(true); if (!owner.Resources) return 7;
                owner.GetLoadedModules(); if (owner.Resources) return 8;
                var count = 0; foreach (var item in owner.Modules) count++;
                if (count != 2 || !owner.Resources) return 9;
                ModuleResolveEventHandler resolve = (sender,e) => first;
                if (!ReferenceEquals(resolve(owner, new ResolveEventArgs("X")), first)) return 10;
                return input + 1;
            }
            private sealed class EmptyModule : Module { }
            private sealed class EmptyAssembly : Assembly { }
            private sealed class CustomModule : Module
            {
                public readonly Type[] Types;
                public CustomModule(Type[] types) { Types = types; }
                public override Type[] GetTypes() => Types;
                public override string ScopeName => "scope";
            }
            private sealed class CustomAssembly : Assembly
            {
                public Module[] Items;
                public bool Resources;
                public CustomAssembly(Module[] modules) { Items = modules; }
                public override Module[] GetModules(bool resources) { Resources = resources; return Items; }
                public override Module[] GetLoadedModules(bool resources) { Resources = resources; return Items; }
            }
        }
        """;
}
