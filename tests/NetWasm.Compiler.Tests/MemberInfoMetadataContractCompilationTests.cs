using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class MemberInfoMetadataContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void MemberMetadataDefaultsRejectAndOverridesDispatch(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("MemberMetadataFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "MemberMetadataFixture.EntryPoint",
            "Run", [new("run", "MemberMetadataFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Reflection;
        namespace MemberMetadataFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                MemberInfo missing = new Missing();
                var rejected = 0;
                try { _ = missing.ReflectedType; } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) rejected++; }
                try { _ = missing.Module; } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) rejected++; }
                try { _ = missing.MetadataToken; } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) rejected++; }
                try { missing.HasSameMetadataDefinitionAs(missing); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) rejected++; }
                if (rejected != 4) return 1;
                var module = new Mod();
                MemberInfo supplied = new Supplied(module);
                if (supplied.ReflectedType != typeof(int) || !ReferenceEquals(supplied.Module, module) || supplied.MetadataToken != 123) return 2;
                if (!supplied.HasSameMetadataDefinitionAs(supplied) || supplied.HasSameMetadataDefinitionAs(missing)) return 3;
                return input + 1;
            }
            private sealed class Missing : MemberInfo { }
            private sealed class Mod : Module { }
            private sealed class Supplied : MemberInfo
            {
                private readonly Module module;
                public Supplied(Module module) { this.module = module; }
                public override Type ReflectedType => typeof(int);
                public override Module Module => module;
                public override int MetadataToken => 123;
                public override bool HasSameMetadataDefinitionAs(MemberInfo other) => ReferenceEquals(this, other);
            }
        }
        """;
}
