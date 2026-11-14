using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Sdk.Pack.References;
using Xunit;

namespace NetWasm.Sdk.Pack.Tests;

// The framework facades: a contract NetWasm.App owns completely gets one; a partial one only when it is a listed
// legacy contract; a shipped assembly, netstandard and a contract with no owned type get none.
public sealed class GenerateFrameworkFacadesTaskTests
{
    [Fact]
    public void FacadesForCompleteAndLegacyContractsOnly()
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-facades-");
        try
        {
            string Write(string name, params string[] types)
            {
                var path = Path.Combine(directory.FullName, "in", name + ".dll");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, StandardFacadeBuilderTests.Assembly(name, types));
                return path;
            }
            var owners = new[] { Write("NetWasm.CoreLib", "System.Object", "System.Buffers.ArrayPool`1"), Write("System.Collections", "System.Collections.Generic.List`1") };
            var contracts = new[]
            {
                Write("System.Buffers", "System.Buffers.ArrayPool`1"),
                Write("System.Runtime", "System.Object", "System.ArgIterator"),
                Write("System.Drawing", "System.Object", "System.Drawing.Bitmap"),
                Write("System.Threading.Channels", "System.Threading.Channels.Channel"),
                Write("netstandard", "System.Object"),
            };
            var task = new GenerateFrameworkFacadesTask
            {
                BuildEngine = new Engine(),
                Contracts = [.. contracts.Select(path => new TaskItem(path))],
                Owners = [.. owners.Select(path => new TaskItem(path))],
                LegacyContracts = [new TaskItem("System.Runtime")],
                OutputDirectory = Path.Combine(directory.FullName, "out"),
            };

            Assert.True(task.Execute());
            Assert.Equal(["System.Buffers", "System.Runtime"], task.Facades.Select(item => Path.GetFileNameWithoutExtension(item.ItemSpec)));
            Assert.Equal("1", task.Facades[1].GetMetadata("Forwarded"));
            Assert.Equal("2", task.Facades[1].GetMetadata("ContractTypes"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class Engine : IBuildEngine
    {
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "";
        public bool BuildProjectFile(string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs) => false;
        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public void LogErrorEvent(BuildErrorEventArgs e) => throw new InvalidOperationException(e.Message);
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogWarningEvent(BuildWarningEventArgs e) { }
    }
}
