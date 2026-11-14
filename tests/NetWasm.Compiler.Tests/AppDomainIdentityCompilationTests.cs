using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class AppDomainIdentityCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CurrentDomainIdentityAndDataMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("DomainFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "DomainFixture.EntryPoint",
            "Run", [new("run", "DomainFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        #pragma warning disable SYSLIB0003, SYSLIB0024, CS0618
        namespace DomainFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var result = Portable(input); if (result != 42) return result;
                try { AppDomain.Unload(AppDomain.CurrentDomain); return 30; } catch (PlatformNotSupportedException e) { if (!e.Message.Contains("unloading")) return 31; }
                if (AppDomain.CurrentDomain.FriendlyName != "DefaultDomain") return 32;
                return input + 1;
            }
            public static int Portable(int input)
            {
                var domain = AppDomain.CurrentDomain;
                if (!ReferenceEquals(domain, AppDomain.CurrentDomain) || domain.Id != 1 || !domain.IsDefaultAppDomain() || domain.IsFinalizingForUnload()) return 1;
                if (!domain.IsFullyTrusted || !domain.IsHomogenous || domain.ShadowCopyFiles || domain.RelativeSearchPath != null || domain.DynamicDirectory != null) return 2;
                if (domain.BaseDirectory != AppContext.BaseDirectory || string.IsNullOrEmpty(domain.FriendlyName) || !domain.ToString().Contains(domain.FriendlyName)) return 3;
                var marker = new object(); const string key = "NetWasm.Qualification.DomainData";
                domain.SetData(key, marker); if (!ReferenceEquals(marker, AppContext.GetData(key)) || !ReferenceEquals(marker, domain.GetData(key))) return 4;
                AppContext.SetData(key, null); if (domain.GetData(key) != null) return 5;
                if (domain.IsCompatibilitySwitchSet("NetWasm.Qualification.UnsetSwitch") != null) return 6;
                AppContext.SetSwitch("NetWasm.Qualification.DomainSwitch", false);
                if (domain.IsCompatibilitySwitchSet("NetWasm.Qualification.DomainSwitch") != false) return 7;
                AppContext.SetSwitch("NetWasm.Qualification.DomainSwitch", true);
                if (domain.IsCompatibilitySwitchSet("NetWasm.Qualification.DomainSwitch") != true) return 8;
                if (domain.ApplyPolicy("Assembly, Version=1.0.0.0") != "Assembly, Version=1.0.0.0") return 9;
                try { domain.ApplyPolicy(null!); return 10; } catch (ArgumentNullException e) { if (e.ParamName != "assemblyName") return 11; }
                try { domain.ApplyPolicy(""); return 12; } catch (ArgumentException) { }
                try { domain.ApplyPolicy("\0invalid"); return 13; } catch (ArgumentException) { }
                try { domain.GetData(null!); return 14; } catch (ArgumentNullException) { }
                try { domain.SetData(null!, null); return 15; } catch (ArgumentNullException) { }
                try { domain.IsCompatibilitySwitchSet(""); return 16; } catch (ArgumentException) { }
                try { AppDomain.CreateDomain(null!); return 17; } catch (ArgumentNullException e) { if (e.ParamName != "friendlyName") return 18; }
                try { AppDomain.CreateDomain("other"); return 19; } catch (PlatformNotSupportedException) { }
                try { AppDomain.Unload(null!); return 20; } catch (ArgumentNullException e) { if (e.ParamName != "domain") return 21; }
                if (AppDomain.GetCurrentThreadId() != Environment.CurrentManagedThreadId) return 22;
                return input + 1;
            }
        }
        """;
}
