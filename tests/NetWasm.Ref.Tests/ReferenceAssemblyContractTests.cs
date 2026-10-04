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
    public void CoreLibReferenceAssemblyDeclaresClsCompliance()
    {
        using var stream = File.OpenRead(FindReferenceAssembly());
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();
        var assembly = metadata.GetAssemblyDefinition();

        Assert.Contains(assembly.GetCustomAttributes(), handle =>
        {
            var attribute = metadata.GetCustomAttribute(handle);
            var declaringType = attribute.Constructor.Kind switch
            {
                HandleKind.MethodDefinition => metadata
                    .GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor)
                    .GetDeclaringType(),
                HandleKind.MemberReference => metadata
                    .GetMemberReference((MemberReferenceHandle)attribute.Constructor)
                    .Parent,
                _ => default,
            };
            if (!IsType(
                    metadata,
                    declaringType,
                    "System",
                    "CLSCompliantAttribute"))
            {
                return false;
            }

            var value = metadata.GetBlobBytes(attribute.Value);
            return value.Length >= 3 && value[0] == 1 && value[1] == 0 &&
                value[2] == 1;
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

    [Fact]
    public void RefPackUsesSdkSelectedProducerOnlyDownloadAndHashCheckedAnalyzerInputs()
    {
        var root = FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(root, "src/NetWasm.Ref/NetWasm.Ref.csproj"));
        var targets = XDocument.Load(Path.Combine(root, "src/NetWasm.Ref/NetWasm.Ref.ImportGenerator.targets"));
        var selection = targets.Descendants("_NetWasmRefGeneratorReferencePack").Single();
        var targetingPackRoot = targets.Descendants("NetCoreTargetingPackRoot").Single();
        var pruningRoots = targets.Descendants("PrunePackageTargetingPackRoots").Single();
        var validation = targets.Descendants("Target").Single(target =>
            (string?)target.Attribute("Name") == "NetWasmRefValidateGeneratorReferencePack");
        var assets = targets.Descendants("Target").Single(target =>
            (string?)target.Attribute("Name") == "NetWasmRefResolveImportGeneratorAssets");
        var pack = project.Descendants("Target").Single(target =>
            (string?)target.Attribute("Name") == "NetWasmRefPack");

        Assert.Contains("KnownFrameworkReference->WithMetadataValue", (string?)selection.Attribute("Include"), StringComparison.Ordinal);
        Assert.Contains("import-generator-targeting-packs", targetingPackRoot.Value, StringComparison.Ordinal);
        Assert.Equal("$(NetCoreTargetingPackRoot)", pruningRoots.Value);
        Assert.Equal("'$(PrunePackageTargetingPackRoots)' == ''", (string?)pruningRoots.Attribute("Condition"));
        Assert.True(pruningRoots.IsBefore(targetingPackRoot));
        Assert.Equal("ProcessFrameworkReferences;CollectPackageDownloads", (string?)validation.Attribute("BeforeTargets"));
        Assert.Contains("ResolveReferences", (string?)assets.Attribute("DependsOnTargets"), StringComparison.Ordinal);
        Assert.Contains("NetWasmRefResolveImportGeneratorAssets", (string?)pack.Attribute("DependsOnTargets"), StringComparison.Ordinal);
        Assert.Equal("false", (string?)assets.Descendants("Unzip").Single().Attribute("SkipUnchangedFiles"));
        Assert.Equal(
            (string?)assets.Descendants("Unzip").Single().Attribute("DestinationFolder"),
            (string?)assets.Descendants("RemoveDir").Single().Attribute("Directories"));
        Assert.True(assets.Descendants("RemoveDir").Single().IsBefore(assets.Descendants("Unzip").Single()));
        Assert.Contains(assets.Descendants("GetFileHash"), hash =>
            (string?)hash.Attribute("MetadataName") == "ExpectedSha256");
        Assert.Contains(pack.Descendants("_NetWasmRefPackageFile"), file =>
            (string?)file.Attribute("Include") == "@(_NetWasmRefGeneratorPackageFile)");
        Assert.Empty(targets.Descendants("PackageReference"));
        Assert.Empty(targets.Descendants("PackageDownload"));
        Assert.Empty(targets.Descendants("Exec"));
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

    private static bool IsType(
        MetadataReader metadata,
        EntityHandle handle,
        string expectedNamespace,
        string expectedName)
    {
        var (typeNamespace, typeName) = handle.Kind switch
        {
            HandleKind.TypeDefinition => (
                metadata.GetString(metadata
                    .GetTypeDefinition((TypeDefinitionHandle)handle).Namespace),
                metadata.GetString(metadata
                    .GetTypeDefinition((TypeDefinitionHandle)handle).Name)),
            HandleKind.TypeReference => (
                metadata.GetString(metadata
                    .GetTypeReference((TypeReferenceHandle)handle).Namespace),
                metadata.GetString(metadata
                    .GetTypeReference((TypeReferenceHandle)handle).Name)),
            _ => (string.Empty, string.Empty),
        };
        return typeNamespace == expectedNamespace && typeName == expectedName;
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
