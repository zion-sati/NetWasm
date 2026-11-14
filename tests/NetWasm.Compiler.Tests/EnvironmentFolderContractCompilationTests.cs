using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class EnvironmentFolderContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void FolderContractsFollowLinuxWithoutHome(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("EnvironmentFolderFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "EnvironmentFolderFixture.EntryPoint",
            "Run", [new("run", "EnvironmentFolderFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        namespace EnvironmentFolderFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if ((int)Environment.SpecialFolder.Desktop != 0 || (int)Environment.SpecialFolder.ApplicationData != 26 || (int)Environment.SpecialFolder.LocalApplicationData != 28) return 1;
                if (Environment.SpecialFolder.MyDocuments != Environment.SpecialFolder.Personal) return 2;
                if ((int)Environment.SpecialFolderOption.None != 0 || (int)Environment.SpecialFolderOption.Create != 32768 || (int)Environment.SpecialFolderOption.DoNotVerify != 16384) return 3;
                var caught = 0;
                // Without HOME, and with no user database on WASI, user folders fall back to "/", as upstream's do; a
                // verified folder that does not exist is empty.
                if (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify) == "/") caught++;
                if (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify) == "/.config") caught++;
                if (Environment.GetFolderPath(Environment.SpecialFolder.Desktop) == "") caught++;
                if (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) == "") caught++;
                if (Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolderOption.DoNotVerify) != "/usr/share") return 6;
                if (Environment.ProcessorCount != 1 || Environment.CurrentManagedThreadId != 1 || Environment.NewLine != "\n") return 4;
                foreach (var invalid in new[] { -1, 1, 49152 })
                    try { Environment.GetFolderPath(Environment.SpecialFolder.Desktop, (Environment.SpecialFolderOption)invalid); }
                    catch (ArgumentOutOfRangeException e) { if (e.ParamName == "option") caught++; }
                return caught == 7 ? input + 1 : 5;
            }
        }
        """;
}
