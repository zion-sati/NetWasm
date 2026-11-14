using System.Xml.Linq;

namespace NetWasm.Ref.Tests;

// NetWasm.App.Runtime carries every NetWasm.App library's implementation and symbols under framework/,
// outside NuGet's asset folders, and lists them for the NetWasm compiler through buildTransitive.
public sealed class AppRuntimePackTests
{
    private static string Root => FindRepositoryRoot();

    [Fact]
    public void PacksEveryImplementationAndItsSymbolsUnderFramework()
    {
        var project = XDocument.Load(Path.Combine(Root, "src/NetWasm.App.Runtime/NetWasm.App.Runtime.csproj"));
        var pack = project.Descendants("Target").Single(target => (string?)target.Attribute("Name") == "NetWasmAppRuntimePack");
        var frameworkFiles = pack.Descendants("_NetWasmAppRuntimeFrameworkFile").Single();

        Assert.Equal("$(MSBuildThisFileDirectory)../libraries/*/*.csproj",
            (string?)project.Descendants("NetWasmAppLibrary").Single().Attribute("Include"));
        Assert.Equal("@(_NetWasmAppImplementation);@(_NetWasmAppImplementationSymbols)", (string?)frameworkFiles.Attribute("Include"));
        Assert.Contains(pack.Descendants("PackagePath"), path =>
            path.Value == "framework/%(_NetWasmAppRuntimeFrameworkFile.Filename)%(_NetWasmAppRuntimeFrameworkFile.Extension)");
        Assert.Contains(pack.Descendants("PackagePath"), path => path.Value == "buildTransitive/NetWasm.App.Runtime.targets");
        Assert.DoesNotContain(pack.Descendants("PackagePath"), path => path.Value.StartsWith("lib/", StringComparison.Ordinal) ||
            path.Value.StartsWith("ref/", StringComparison.Ordinal));
        Assert.Contains(pack.Descendants("Error"), error => (string?)error.Attribute("Code") == "NWRF011");
        Assert.Equal("NetWasmAppRuntimePack", project.Descendants("NetWasmSdkContractPackTarget").Single().Value);
    }

    [Fact]
    public void ListsTheImplementationsForTheCompilerInANetwasmBuild()
    {
        var targets = XDocument.Load(Path.Combine(Root, "src/NetWasm.App.Runtime/buildTransitive/NetWasm.App.Runtime.targets"));
        var group = targets.Descendants("ItemGroup").Single(itemGroup => itemGroup.Element("NetWasmFrameworkAssembly") is not null);

        Assert.Equal("'$(TargetFramework)' == 'netwasm0.1'", (string?)group.Attribute("Condition"));
        Assert.Equal("$(MSBuildThisFileDirectory)../framework/*.dll",
            (string?)group.Element("NetWasmFrameworkAssembly")!.Attribute("Include"));
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
