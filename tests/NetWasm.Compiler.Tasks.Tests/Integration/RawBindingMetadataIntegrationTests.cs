using System.Diagnostics;
using System.Xml.Linq;

namespace NetWasm.Compiler.Tasks.Tests.Integration;

public sealed class RawBindingMetadataIntegrationTests
{
    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    [Trait("Category", "NativeMetadataIntegration")]
    public async Task UnchangedRawBuildRetainsComputedImportRequirements(string target)
    {
        var retained = Environment.GetEnvironmentVariable("NETWASM_NATIVE_INTEROP_EVIDENCE_ROOT");
        var root = retained is null ? Directory.CreateTempSubdirectory("netwasm-raw-metadata-").FullName :
            Path.Combine(retained, "raw-metadata-" + target);
        if (retained is not null)
        {
            Assert.False(Directory.Exists(root));
            Directory.CreateDirectory(root);
        }
        try
        {
            var assets = Path.Combine(AppContext.BaseDirectory, "IntegrationAssets");
            File.Copy(Path.Combine(assets, "global.json"), Path.Combine(root, "global.json"));
            var document = XDocument.Load(Path.Combine(assets, "NetWasm.Compiler.Tasks.targets"));
            var xml = document.Root!.Name.Namespace;
            var bindingTarget = Assert.Single(document.Root.Elements(xml + "Target"),
                element => (string?)element.Attribute("Name") == "NetWasmCompilerTasksBuildRawBindings");
            var invocation = bindingTarget.Element(xml + "NetWasmBuildRawBindingsTask")!;
            invocation.Name = xml + nameof(RawBindingMetadataProducerTask);
            invocation.RemoveAttributes();
            invocation.SetAttributeValue("AdapterPath", "$(NetWasmRawAdapterPath)");
            document.Root.AddFirst(new XElement(xml + "UsingTask",
                new XAttribute("TaskName", typeof(RawBindingMetadataProducerTask).FullName!),
                new XAttribute("AssemblyFile", typeof(RawBindingMetadataProducerTask).Assembly.Location)));
            var targets = Path.Combine(root, "Compiler.targets");
            document.Save(targets);
            var input = Path.Combine(root, "input");
            await File.WriteAllTextAsync(input, "unchanged binding input");
            var adapter = Path.Combine(root, "adapter.mjs");
            var observation = Path.Combine(root, "imports.txt");
            var project = Path.Combine(root, "RawBindings.proj");
            await File.WriteAllTextAsync(project, """
                <Project DefaultTargets="Probe">
                  <PropertyGroup>
                    <TargetFramework>netwasm0.1</TargetFramework>
                    <OutputType>Exe</OutputType>
                    <NetWasmRawWasm>true</NetWasmRawWasm>
                    <NetWasmCompilerMetadataPath>$(ProbeInput)</NetWasmCompilerMetadataPath>
                    <NetWasmInteropManifestPath>$(ProbeInput)</NetWasmInteropManifestPath>
                    <NetWasmCompilerWitPath>$(ProbeInput)</NetWasmCompilerWitPath>
                    <NetWasmWitPath>$(ProbeInput)</NetWasmWitPath>
                    <NetWasmOutputPath>$(ProbeInput)</NetWasmOutputPath>
                    <NetWasmRawInspectionScriptPath>$(ProbeInput)</NetWasmRawInspectionScriptPath>
                    <NetWasmBinaryenModulePath>$(ProbeInput)</NetWasmBinaryenModulePath>
                    <NetWasmWasmToolsCommandPath>$(ProbeInput)</NetWasmWasmToolsCommandPath>
                    <NetWasmWasmToolsModulePath>$(ProbeInput)</NetWasmWasmToolsModulePath>
                    <NetWasmNodePath>$(ProbeInput)</NetWasmNodePath>
                  </PropertyGroup>
                  <Import Project="$(CompilerTargets)" />
                  <Target Name="NetWasmCompilerTasksLinkRawModule" />
                  <Target Name="Probe" DependsOnTargets="NetWasmCompilerTasksBuildRawBindings">
                    <WriteLinesToFile File="$(ObservationPath)" Overwrite="true"
                      Lines="@(_NetWasmRequiredImport->'%(Interface)|%(Name)|%(Parameters)|%(Results)');@(_NetWasmRequiredImportModule->'%(Identity)')" />
                  </Target>
                </Project>
                """);

            await RunAsync(1);
            var first = await File.ReadAllLinesAsync(observation);
            Assert.Equal([
                "sample:api/math@1|sum|[{\"name\":\"value\",\"type\":\"u32\"}]|[\"u32\"]",
                "sample:api/math@1",
            ], first);
            Assert.True(File.Exists(adapter));
            var inputTime = File.GetLastWriteTimeUtc(input);
            Assert.True(File.GetLastWriteTimeUtc(adapter) >= inputTime);

            await RunAsync(2);
            Assert.Equal(first, await File.ReadAllLinesAsync(observation));
            Assert.Equal(inputTime, File.GetLastWriteTimeUtc(input));

            async Task RunAsync(int run)
            {
                var start = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var argument in new[]
                {
                    "msbuild", project, "-nologo", "-v:q",
                    "-p:CompilerTargets=" + targets, "-p:ProbeInput=" + input,
                    "-p:NetWasmRawAdapterPath=" + adapter, "-p:ObservationPath=" + observation,
                    "-p:NetWasmTarget=" + target,
                }) start.ArgumentList.Add(argument);
                await using var stdout = File.Create(Path.Combine(root, $"msbuild-{run}.stdout.log"));
                await using var stderr = File.Create(Path.Combine(root, $"msbuild-{run}.stderr.log"));
                using var process = Process.Start(start)!;
                await Task.WhenAll(process.StandardOutput.BaseStream.CopyToAsync(stdout),
                    process.StandardError.BaseStream.CopyToAsync(stderr), process.WaitForExitAsync());
                Assert.Equal(0, process.ExitCode);
            }
        }
        finally
        {
            if (retained is null) Directory.Delete(root, recursive: true);
        }
    }
}
