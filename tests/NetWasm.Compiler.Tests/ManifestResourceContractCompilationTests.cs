using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ManifestResourceContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ResourceMetadataStoresValuesAndDiscoveryRejects(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ManifestResourceFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ManifestResourceFixture.EntryPoint",
            "Run", [new("run", "ManifestResourceFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Reflection;
        namespace ManifestResourceFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (Portable(input) != 42) return 20;
                Assembly assembly = new FakeAssembly();
                try { assembly.GetManifestResourceInfo("item"); return 21; } catch (PlatformNotSupportedException e) { if (e.Message.Length == 0) return 22; }
                return input + 1;
            }
            public static int Portable(int input)
            {
                Assembly assembly = new FakeAssembly();
                var flags = ResourceLocation.Embedded | ResourceLocation.ContainedInManifestFile;
                var info = new ManifestResourceInfo(assembly, "resource.bin", flags);
                if (!ReferenceEquals(info.ReferencedAssembly, assembly) || info.FileName != "resource.bin" || info.ResourceLocation != flags) return 1;
                var empty = new ManifestResourceInfo(null, null, (ResourceLocation)99);
                if (empty.ReferencedAssembly != null || empty.FileName != null || (int)empty.ResourceLocation != 99) return 2;
                if ((int)ResourceLocation.Embedded != 1 || (int)ResourceLocation.ContainedInAnotherAssembly != 2 || (int)ResourceLocation.ContainedInManifestFile != 4) return 3;
                ManifestResourceInfo custom = new CustomInfo(assembly);
                if (!ReferenceEquals(custom.ReferencedAssembly, assembly) || custom.FileName != "override" || custom.ResourceLocation != ResourceLocation.ContainedInAnotherAssembly) return 4;
                return input + 1;
            }
            private sealed class FakeAssembly : Assembly { }
            private sealed class CustomInfo : ManifestResourceInfo
            {
                private readonly Assembly _assembly;
                public CustomInfo(Assembly assembly) : base(null, null, 0) { _assembly = assembly; }
                public override Assembly ReferencedAssembly => _assembly;
                public override string FileName => "override";
                public override ResourceLocation ResourceLocation => ResourceLocation.ContainedInAnotherAssembly;
            }
        }
        """;
}
