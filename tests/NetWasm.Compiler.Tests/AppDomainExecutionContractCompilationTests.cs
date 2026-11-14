using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class AppDomainExecutionContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void UnsupportedExecutionRejectsWithoutMutation(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("DomainExecutionFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "DomainExecutionFixture.EntryPoint",
            "Run", [new("run", "DomainExecutionFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Configuration.Assemblies;
        namespace DomainExecutionFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (Portable(input) != 42) return 20;
                var domain = AppDomain.CurrentDomain;
                var before = domain.BaseDirectory;
                var args = new[] { "original" }; var hash = new byte[] { 1, 2 };
                var rejected = 0;
                try { domain.ExecuteAssembly("missing.dll"); } catch (PlatformNotSupportedException) { rejected++; }
                try { domain.ExecuteAssembly("missing.dll", args); } catch (PlatformNotSupportedException) { rejected++; }
                try { domain.ExecuteAssembly("missing.dll", args, hash, AssemblyHashAlgorithm.SHA256); } catch (PlatformNotSupportedException) { rejected++; }
                try { domain.ExecuteAssemblyByName("Missing"); } catch (PlatformNotSupportedException) { rejected++; }
                try { domain.ExecuteAssemblyByName("Missing", args); } catch (PlatformNotSupportedException) { rejected++; }
                domain.AppendPrivatePath("unused");
                domain.ClearPrivatePath();
                domain.ClearShadowCopyPath();
                domain.SetCachePath("unused");
                domain.SetDynamicBase("unused");
                domain.SetShadowCopyFiles();
                domain.SetShadowCopyPath("unused");
                if (rejected != 5 || domain.BaseDirectory != before || domain.ShadowCopyFiles || domain.RelativeSearchPath != null || domain.DynamicDirectory != null) return 21;
                if (args[0] != "original" || hash[0] != 1 || hash[1] != 2) return 22;
                return input + 1;
            }
            public static int Portable(int input)
            {
                var domain = AppDomain.CurrentDomain;
                try { domain.ExecuteAssembly(null!); return 1; } catch (ArgumentNullException e) { if (e.ParamName != "assemblyFile") return 2; }
                try { domain.ExecuteAssembly(null!, null); return 3; } catch (ArgumentNullException) { }
                try { domain.ExecuteAssemblyByName((string)null!); return 4; } catch (ArgumentNullException) { }
                try { domain.ExecuteAssemblyByName((string)null!, null); return 5; } catch (ArgumentNullException) { }
                return input + 1;
            }
        }
        """;
}
