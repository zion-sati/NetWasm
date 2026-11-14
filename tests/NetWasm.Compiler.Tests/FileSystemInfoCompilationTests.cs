using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class FileSystemInfoCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    // A FileSystemInfo subclass's path members are portable: they never reach the file system.
    // The members that read the file system are upstream's Unix code over the platform's file
    // system, which FileApisWorkOnAPreopenedDirectoryInWasmtime and
    // FileApiEdgeCasesMatchDesktopDotNetInWasmtime cover against real files.
    public void SubclassPathMembersArePortable(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "FileSystemFixture", Source, "FileSystemFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.IO;
        namespace FileSystemFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var paths = new string[] { "/data/file.txt", "/data/file.", "/data/.hidden", "/data.with.dot/file", "/data/file", "/data.with.dot/", "" };
                var extensions = new string[] { ".txt", ".", ".hidden", "", "", "", "" };
                for (var i = 0; i < paths.Length; i++)
                {
                    FileSystemInfo info = new Entry(paths[i], "original");
                    if (info.FullName != paths[i] || info.Extension != extensions[i] || info.ToString() != "original") return 5;
                    if (!info.Exists) return 6;
                    info.Delete();
                    if (info.Exists) return 7;
                    MarshalByRefObject asBase = info;
                    if (!ReferenceEquals(asBase, info)) return 8;
                }
                FileSystemInfo named = new Entry("/data/file.txt", "relative.txt");
                if (named.Name != "file.txt" || named.ToString() != "relative.txt") return 9;
                return input + 1;
            }
        }
        public sealed class Entry : FileSystemInfo
        {
            private bool exists = true;
            public Entry(string full, string original) { FullPath = full; OriginalPath = original; }
            public override string Name => Path.GetFileName(FullPath);
            public override bool Exists => exists;
            public override void Delete() { exists = false; }
        }
        """;
}
