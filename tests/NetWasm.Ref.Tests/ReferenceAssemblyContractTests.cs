using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace NetWasm.Ref.Tests;

public sealed class ReferenceAssemblyContractTests
{
    [Fact]
    public void CoreLibReferenceAssemblyContainsMetadataWithoutMethodBodies()
    {
        var path = FindReferenceAssembly();

        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();

        Assert.True(metadata.TypeDefinitions.Count > 0);

        foreach (var handle in metadata.MethodDefinitions)
        {
            var rva = metadata.GetMethodDefinition(handle).RelativeVirtualAddress;
            if (rva == 0)
            {
                continue;
            }

            var il = reader.GetMethodBody(rva).GetILBytes();
            Assert.Equal([0x14, 0x7A], il);
        }
    }

    [Fact]
    public void CoreLibReferenceAssemblyContainsAssemblyMetadataAttribute()
    {
        using var stream = File.OpenRead(FindReferenceAssembly());
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();

        Assert.Contains(metadata.TypeDefinitions, handle =>
        {
            var definition = metadata.GetTypeDefinition(handle);
            return metadata.GetString(definition.Namespace) == "System.Reflection"
                && metadata.GetString(definition.Name) == "AssemblyMetadataAttribute";
        });
    }

    [Fact]
    public void CoreLibReferenceAssemblyRetainsExpectedIdentity()
    {
        var path = FindReferenceAssembly();

        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();
        var assembly = metadata.GetAssemblyDefinition();

        Assert.Equal("NetWasm.CoreLib", metadata.GetString(assembly.Name));
    }

    [Fact]
    public void CoreLibReferenceAssemblyDoesNotContainRegexTypes()
    {
        using var stream = File.OpenRead(FindReferenceAssembly());
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();

        Assert.DoesNotContain(metadata.TypeDefinitions, handle =>
        {
            var definition = metadata.GetTypeDefinition(handle);
            return metadata.GetString(definition.Namespace).StartsWith(
                "System.Text.RegularExpressions",
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void RefPackRequestForwardsStandardPackageMetadata()
    {
        var project = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src/NetWasm.Ref/NetWasm.Ref.csproj"));
        var licenseExpression = project.Descendants("PackageLicenseExpression").Single();
        var packRequest = project.Descendants("CanonicalPackMsBuildTask").Single();

        Assert.Equal("MIT", licenseExpression.Value);
        Assert.Equal("$(PackageLicenseExpression)", (string?)packRequest.Attribute("PackageLicenseExpression"));
        Assert.Equal("$(PackageLicenseFile)", (string?)packRequest.Attribute("PackageLicenseFile"));
        Assert.Equal("$(PackageReadmeFile)", (string?)packRequest.Attribute("PackageReadmeFile"));
        Assert.Equal("$(PackageOwners)", (string?)packRequest.Attribute("PackageOwners"));
        Assert.Equal("$(PackageProjectUrl)", (string?)packRequest.Attribute("PackageProjectUrl"));
        Assert.Equal("$(Copyright)", (string?)packRequest.Attribute("PackageCopyright"));
        Assert.Equal("$(PackageTags)", (string?)packRequest.Attribute("PackageTags"));
        Assert.Equal("$(RepositoryUrl)", (string?)packRequest.Attribute("RepositoryUrl"));
        Assert.Equal("$(RepositoryType)", (string?)packRequest.Attribute("RepositoryType"));
        Assert.Equal("$(PublishRepositoryUrl)", (string?)packRequest.Attribute("PublishRepositoryUrl"));
    }

    private static string FindReferenceAssembly()
    {
        var configuration = typeof(ReferenceAssemblyContractTests).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
            ?? throw new InvalidOperationException("The test assembly does not declare its build configuration.");

        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            $"../../../../../src/NetWasm.CoreLib/bin/{configuration}/net10.0/ref/NetWasm.CoreLib.dll"));
        Assert.True(File.Exists(path));
        return path;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src/NetWasm.Ref/NetWasm.Ref.csproj")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
