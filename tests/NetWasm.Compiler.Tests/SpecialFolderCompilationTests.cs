using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Environment.GetFolderPath resolves special folders from HOME and the XDG variables, as RC3's Unix code does for
// WASI. The expected paths are desktop .NET 11's for HOME=/home/test with no XDG variables set.
public sealed class SpecialFolderCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SpecialFoldersFollowHomeAsOnLinux(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "FoldersFixture", Source, "FoldersFixture.EntryPoint", false, target, 41, [])
        {
            Optimization = FinalWasmOptimization.None,
            // A folder is verified through the file system.
            Exports = [new RequestedExport("run", "FoldersFixture.EntryPoint", "Run")],
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/platform@1.0.0",
            ExpectedEnvironmentReads = 1,
            Environment = ImmutableDictionary<string, string>.Empty.Add("HOME", "/home/test"),
        }));
    }

    private const string Source = """
        using System;
        namespace FoldersFixture;
        public static class EntryPoint
        {
            private static readonly (Environment.SpecialFolder Folder, string Path)[] Expected =
            [
                (Environment.SpecialFolder.Desktop, "/home/test/Desktop"),
                (Environment.SpecialFolder.MyDocuments, "/home/test/Documents"),
                (Environment.SpecialFolder.MyMusic, "/home/test/Music"),
                (Environment.SpecialFolder.MyVideos, "/home/test/Videos"),
                (Environment.SpecialFolder.DesktopDirectory, "/home/test/Desktop"),
                (Environment.SpecialFolder.Fonts, "/home/test/.fonts"),
                (Environment.SpecialFolder.Templates, "/home/test/Templates"),
                (Environment.SpecialFolder.ApplicationData, "/home/test/.config"),
                (Environment.SpecialFolder.LocalApplicationData, "/home/test/.local/share"),
                (Environment.SpecialFolder.CommonApplicationData, "/usr/share"),
                (Environment.SpecialFolder.MyPictures, "/home/test/Pictures"),
                (Environment.SpecialFolder.UserProfile, "/home/test"),
                (Environment.SpecialFolder.CommonTemplates, "/usr/share/templates"),
                (Environment.SpecialFolder.System, ""),
            ];

            public static int Run(int input)
            {
                for (var i = 0; i < Expected.Length; i++)
                {
                    if (Environment.GetFolderPath(Expected[i].Folder, Environment.SpecialFolderOption.DoNotVerify) != Expected[i].Path) return 10 + i;
                }
                // Verified: a folder that does not exist is empty.
                if (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) != "") return 1;
                try { Environment.GetFolderPath((Environment.SpecialFolder)12345); return 2; }
                catch (ArgumentOutOfRangeException) { }
                try { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, (Environment.SpecialFolderOption)7); return 3; }
                catch (ArgumentOutOfRangeException e) when (e.ParamName == "option") { }
                return input + 1;
            }
        }
        """;
}
