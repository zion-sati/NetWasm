using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace NetWasm.Sdk.Tests.Integration;

public sealed class NativeLibraryProjectReferenceIntegrationTests
{
    [Theory]
    [InlineData("wasm32", false, false)]
    [InlineData("wasm64", false, false)]
    [InlineData("wasm32", true, false)]
    [InlineData("wasm64", true, false)]
    [InlineData("wasm32", false, true)]
    [InlineData("wasm64", false, true)]
    [InlineData("wasm32", true, true)]
    [InlineData("wasm64", true, true)]
    [Trait("Category", "NativeProjectReferenceIntegration")]
    public async Task CollectsSourceRequirementsWithoutBuildingAndHonorsRootCustomization(string width, bool testRoot, bool explicitLeafEdge)
    {
        var retained = Environment.GetEnvironmentVariable("NETWASM_NATIVE_INTEROP_EVIDENCE_ROOT");
        var root = retained is null ? Directory.CreateTempSubdirectory("netwasm-native-projects-").FullName :
            Path.Combine(retained, width + (testRoot ? "-test" : "-app") + (explicitLeafEdge ? "-explicit" : "-diamond"));
        if (retained is not null)
        {
            Assert.False(Directory.Exists(root));
            Directory.CreateDirectory(root);
        }
        try
        {
            var assets = Path.Combine(AppContext.BaseDirectory, "IntegrationAssets");
            File.Copy(Path.Combine(assets, "global.json"), Path.Combine(root, "global.json"));
            new XDocument(new XElement("configuration", new XElement("packageSources", new XElement("clear"))))
                .Save(Path.Combine(root, "NuGet.Config"));
            var targets = Path.Combine(root, "NetWasm.NativeLibraries.targets");
            File.Copy(Path.Combine(assets, "NetWasm.NativeLibraries.targets"), targets);
            var observation = Path.Combine(root, "requirements.txt");
            var leaf = WriteProject("leaf with spaces", new("TargetFrameworks", "net10.0;net10.0-windows"), [],
                [Declaration("leaf", "wasm32"), Declaration("leaf", "wasm64")]);
            var missingGetter = WriteProject("plain", new("TargetFramework", "net10.0"), [],
                [Declaration("plain", "wasm32")], importTargets: false);
            var left = WriteProject("left", new("TargetFramework", "net10.0"),
                [Reference(leaf), Reference(missingGetter)],
                [Declaration("left", "$(NetWasmTarget)")]);
            var right = WriteProject("right", new("TargetFramework", "net10.0"), [Reference(leaf)], []);
            var blockedBuild = WriteProject("blocked-build", new("TargetFramework", "net10.0"), [],
                [Declaration("blocked-build", "wasm32")]);
            var blockedDisabled = WriteProject("blocked-disabled", new("TargetFramework", "net10.0"), [],
                [Declaration("blocked-disabled", "wasm32")]);
            var buildOnly = WriteProject("build-only", new("TargetFramework", "net10.0"), [Reference(blockedBuild)],
                [Declaration("build-only", "wasm32")]);
            var disabled = WriteProject("disabled", new("TargetFramework", "net10.0"), [Reference(blockedDisabled)],
                [Declaration("disabled", "wasm32")]);
            var rootReferences = new List<XElement>
                { Reference(left), Reference(right), Reference(buildOnly, "ReferenceOutputAssembly"), Reference(disabled, "BuildReference") };
            if (explicitLeafEdge) rootReferences.Add(Reference(leaf));
            var project = WriteProject("root", new("TargetFramework", "net10.0"),
                [.. rootReferences], []);
            var document = XDocument.Load(project);
            document.Root!.Add(new XElement("PropertyGroup", new XElement("NetWasmTarget", width),
                new XElement("IsTestProject", testRoot ? "true" : "false")));
            document.Root.Add(new XElement("Target", new XAttribute("Name", "CustomizeNativeRequirements"),
                new XAttribute("BeforeTargets", "NetWasmGetNativeLibraries"), new XAttribute("Condition", "'$(ApplyOverride)' == 'true'"),
                new XElement("ItemGroup",
                    new XElement("NativeLibrary", new XAttribute("Remove", "@(NativeLibrary)"),
                        new XAttribute("Condition", "'%(NativeLibrary.NetWasmLibraryName)' == 'leaf' AND '%(NativeLibrary.WasmTarget)' == 'wasm32'")),
                    new XElement("NativeLibrary", new XAttribute("Update", "@(NativeLibrary)"),
                        new XAttribute("Condition", "'%(NativeLibrary.NetWasmLibraryName)' == 'left'"),
                        new XElement("NetWasmLibraryName", Escape("updated%3B;left"))))));
            document.Root.Add(new XElement("Target", new XAttribute("Name", "NetWasmRuntimePackMaterialize"),
                new XElement("WriteLinesToFile", new XAttribute("File", "$(ObservationPath)"),
                    new XAttribute("Lines", "@(NativeLibrary->'%(Identity)|%(NetWasmLibraryName)|%(WasmTarget)|%(SelectedFramework)')"),
                    new XAttribute("Overwrite", "true"))));
            document.Save(project);

            _ = await RunAsync(0, buildReferences: true, customize: false, restoreOnly: true);
            var first = await RunAsync(1, buildReferences: true, customize: false);
            var leafPaths = explicitLeafEdge ? 3 : 2;
            Assert.Equal(leafPaths * 2 + 1, first.Length);
            Assert.Equal(leafPaths, first.Count(item => item.Name == "leaf" && item.Target == "wasm32"));
            Assert.Equal(leafPaths, first.Count(item => item.Name == "leaf" && item.Target == "wasm64"));
            Assert.Equal(width, Assert.Single(first, item => item.Name == "left").Target);
            Assert.All(first, item =>
            {
                Assert.True(Path.IsPathFullyQualified(item.Path));
                Assert.Equal("net10.0", item.Framework);
                Assert.False(File.Exists(item.Path));
            });
            foreach (var target in new[] { "wasm32", "wasm64" })
                Assert.All(first.Where(item => item.Name == "leaf" && item.Target == target), item =>
                    Assert.Equal(Path.Combine(Path.GetDirectoryName(leaf)!, "native", target, "lib%3B;leaf.a"), item.Path));

            var warm = await RunAsync(2, buildReferences: false, customize: false);
            Assert.Equal(first, warm);

            var customized = await RunAsync(3, buildReferences: false, customize: true);
            Assert.Equal(leafPaths + 1, customized.Length);
            Assert.DoesNotContain(customized, item => item.Name == "leaf" && item.Target == "wasm32");
            Assert.Equal(width, Assert.Single(customized, item => item.Name == "updated%3B;left").Target);
            Assert.Empty(Directory.GetDirectories(root, "bin", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFiles(root, "*.dll", SearchOption.AllDirectories));
            await File.WriteAllTextAsync(Path.Combine(root, "receipt.json"), JsonSerializer.Serialize(new
            {
                width,
                testRoot,
                explicitLeafEdge,
                SourceTargetSha256 = Digest(targets),
                GlobalJsonSha256 = Digest(Path.Combine(root, "global.json")),
                TestAssemblySha256 = Digest(typeof(NativeLibraryProjectReferenceIntegrationTests).Assembly.Location),
                Cold = first,
                Warm = warm,
                Customized = customized,
            }));

            string WriteProject(string directory, XElement framework, XElement[] references, XElement[] declarations, bool importTargets = true)
            {
                var path = Path.Combine(root, directory, "Fixture.csproj");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var content = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                    new XElement("PropertyGroup", framework,
                        new XElement("PackageId", "Fixture." + directory.Replace(' ', '.')),
                        new XElement("AssemblyName", "Fixture." + directory.Replace(' ', '.'))), new XElement("ItemGroup", references),
                    new XElement("ItemGroup", declarations));
                if (importTargets) content.Add(new XElement("Import", new XAttribute("Project", targets)));
                new XDocument(content).Save(path);
                return path;
            }

            async Task<Requirement[]> RunAsync(int run, bool buildReferences, bool customize, bool restoreOnly = false)
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
                    "msbuild", project, restoreOnly ? "-t:Restore" : "-t:NetWasmRuntimePackMaterialize", "-nologo", "-v:q", "-nr:false",
                    "-p:ObservationPath=" + observation, "-p:BuildProjectReferences=" + buildReferences,
                    "-p:ApplyOverride=" + customize,
                }) start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!;
                await using var stdout = File.Create(Path.Combine(root, $"msbuild-{run}.stdout.log"));
                await using var stderr = File.Create(Path.Combine(root, $"msbuild-{run}.stderr.log"));
                await Task.WhenAll(process.StandardOutput.BaseStream.CopyToAsync(stdout),
                    process.StandardError.BaseStream.CopyToAsync(stderr), process.WaitForExitAsync());
                Assert.Equal(0, process.ExitCode);
                if (restoreOnly) return [];
                File.Copy(observation, Path.Combine(root, $"requirements-{run}.txt"));
                return (await File.ReadAllLinesAsync(observation)).Select(line =>
                {
                    var fields = line.Split('|');
                    Assert.Equal(4, fields.Length);
                    return new Requirement(fields[0], fields[1], fields[2], fields[3]);
                }).ToArray();
            }
        }
        finally
        {
            if (retained is null) Directory.Delete(root, recursive: true);
        }
    }

    private static XElement Declaration(string name, string target) => new("NativeLibrary",
        new XAttribute("Include", "native/" + target + "/lib" + Escape("%3B;" + name) + ".a"),
        new XElement("NetWasmLibraryName", name), new XElement("WasmTarget", target),
        new XElement("SelectedFramework", "$(TargetFramework)"));

    private static XElement Reference(string path, string? disabledMetadata = null) => new("ProjectReference",
        new XAttribute("Include", path), disabledMetadata is null ? null : new XAttribute(disabledMetadata, "false"));

    private static string Escape(string value) => value.Replace("%", "%25", StringComparison.Ordinal)
        .Replace(";", "%3B", StringComparison.Ordinal);

    private static string Digest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed record Requirement(string Path, string Name, string Target, string Framework);
}
