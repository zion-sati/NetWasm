using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class FileOptionContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void FileOptionEnumsRemainUsableWithoutFilesystem(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "FileOptionFixture", Source, "FileOptionFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.IO;
        namespace FileOptionFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if ((int)FileAccess.ReadWrite != 3 || FileAccess.ReadWrite.ToString() != "ReadWrite") return 1;
                if ((int)FileMode.Append != 6 || FileMode.Open.ToString() != "Open") return 2;
                if ((FileShare.Read | FileShare.Write) != FileShare.ReadWrite || !FileShare.ReadWrite.HasFlag(FileShare.Read)) return 3;
                var options = FileOptions.Asynchronous | FileOptions.WriteThrough;
                if (!options.HasFlag(FileOptions.WriteThrough) || (int)FileOptions.WriteThrough != int.MinValue) return 4;
                var attributes = FileAttributes.Hidden | FileAttributes.ReadOnly;
                if (!attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.Directory)) return 5;
                if (SearchOption.AllDirectories.ToString() != "AllDirectories" || (int)SearchOption.TopDirectoryOnly != 0) return 6;
                if (HandleInheritability.Inheritable.ToString() != "Inheritable" || (int)HandleInheritability.None != 0) return 7;
                return input + 1;
            }
        }
        """;
}
