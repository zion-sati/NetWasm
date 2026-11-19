// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Xml.Linq;

namespace NetWasm.Ref.Tests;

// NetWasm.App.Ref carries every NetWasm.App library's reference assembly, netstandard.dll forwarding each
// standard type to CoreLib or the library that declares it, and the System.Text.Json and Regex generators
// from the exact .NET 11 targeting pack (the libraries are .NET 11's), through the SDK-owned pack boundary NetWasm.Ref
// uses. The project itself still builds for net10.0.
public sealed class AppReferencePackTests
{
    private static XDocument Project() =>
        XDocument.Load(Path.Combine(FindRepositoryRoot(), "src/NetWasm.App.Ref/NetWasm.App.Ref.csproj"));

    private static XElement Target(XDocument project, string name) =>
        project.Descendants("Target").Single(target => (string?)target.Attribute("Name") == name);

    [Fact]
    public void PacksEveryLibraryReferenceAssembly()
    {
        var project = Project();
        var libraries = project.Descendants("_NetWasmAppLibraryProject").Single();
        var contracts = Target(project, "NetWasmAppRefBuildContracts");
        var pack = Target(project, "NetWasmAppRefPack");

        Assert.Equal("$(MSBuildProjectDirectory)/../libraries/*/*.csproj", (string?)libraries.Attribute("Include"));
        Assert.Contains(contracts.Descendants("_NetWasmAppReferenceAssembly"), item =>
            (string?)item.Attribute("Include") == "@(_NetWasmAppLibraryOutput->'%(ReferenceAssembly)')");
        Assert.Contains(contracts.Descendants("Error"), error => (string?)error.Attribute("Code") == "NWRF009");
        // Under both target frameworks: the platform one and the deprecated netwasm0.1.
        var references = pack.Descendants("_NetWasmAppRefPackageFile")
            .Where(file => (string?)file.Attribute("Include") == "@(_NetWasmAppReferenceAssembly)")
            .Select(file => (file.Element("PackagePath")!.Value, file.Element("TargetFrameworkAlias")!.Value));
        Assert.Equal(
            [
                ("ref/NetWasm,Version=v0.1/%(_NetWasmAppReferenceAssembly.Filename)%(_NetWasmAppReferenceAssembly.Extension)", "netwasm0.1"),
                ("ref/net10.0-netwasm/%(_NetWasmAppReferenceAssembly.Filename)%(_NetWasmAppReferenceAssembly.Extension)", "net10.0-netwasm"),
            ],
            references);
    }

    [Fact]
    public void BuildsTheStandardFacadeOverCoreLibAndEveryLibrary()
    {
        var project = Project();
        var facade = Target(project, "NetWasmAppRefGenerateStandardFacade");
        var pack = Target(project, "NetWasmAppRefPack");

        Assert.Equal("$(CoreLibReferenceAssemblyPath);@(_NetWasmAppReferenceAssembly)",
            (string?)facade.Descendants("_NetWasmStandardFacadeOwner").Single().Attribute("Include"));
        Assert.Single(facade.Descendants("GenerateStandardFacadeTask"));
        Assert.Contains("NetWasmAppRefGenerateStandardFacade", (string?)pack.Attribute("DependsOnTargets"), StringComparison.Ordinal);
        Assert.Contains(pack.Descendants("PackagePath"), path => path.Value == "ref/NetWasm,Version=v0.1/netstandard.dll");
        Assert.Contains(project.Descendants("PackageDownload"), download =>
            (string?)download.Attribute("Include") == "NETStandard.Library.Ref");
    }

    [Fact]
    public void ShipsTheJsonAndRegexGeneratorsFromTheNet11TargetingPack()
    {
        var project = Project();
        var pack = Target(project, "NetWasmAppRefPack");

        Assert.Equal("net10.0", project.Descendants("TargetFramework").Single().Value);
        Assert.Equal("net11.0", project.Descendants("NetWasmRefGeneratorTargetFramework").Single().Value);
        Assert.Equal("System.Text.Json.SourceGeneration;System.Text.RegularExpressions.Generator",
            (string?)project.Descendants("NetWasmRefGeneratorAssembly").Single().Attribute("Include"));
        Assert.Equal("source-generators", project.Descendants("NetWasmRefGeneratorProvenanceName").Single().Value);
        Assert.Contains(project.Descendants("Import"), import =>
            (string?)import.Attribute("Project") == "../NetWasm.Ref/NetWasm.Ref.ImportGenerator.targets");
        Assert.Contains("NetWasmRefResolveImportGeneratorAssets", (string?)pack.Attribute("DependsOnTargets"), StringComparison.Ordinal);
        Assert.Contains(pack.Descendants("_NetWasmAppRefPackageFile"), file =>
            (string?)file.Attribute("Include") == "@(_NetWasmRefGeneratorPackageFile)");
    }

    [Fact]
    public void PacksThroughTheSdkOwnedContractBoundary()
    {
        var project = Project();
        var wrapper = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src/NetWasm.Sdk/Sdk/NuGetBuildTasksPackTargets.targets"));

        Assert.Equal("true", project.Descendants("NetWasmSdkRefPackEnabled").Single().Value);
        Assert.Equal("NetWasmAppRefPack", project.Descendants("NetWasmSdkContractPackTarget").Single().Value);
        // The wrapper declares the MSBuild namespace; its elements compare by local name.
        IEnumerable<XElement> Named(string name) => wrapper.Descendants().Where(element => element.Name.LocalName == name);
        Assert.Equal("$(BeforePack);$(NetWasmSdkContractPackTarget)", Named("NetWasmSdkOwnedPackDependsOn").Last().Value);
        Assert.Equal("NetWasmRefPack", Named("NetWasmSdkContractPackTarget").Single().Value);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NetWasm.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("Could not locate the NetWasm repository root.");
    }
}
