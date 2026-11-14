using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Each NetWasm.App library carries its upstream assembly identity, the version and public key of the same
// assembly in the .NET 11.0.0 reference pack the repository's SDK installs, so a package built against Microsoft's
// assembly binds to it.
public sealed class FrameworkAssemblyIdentityTests
{
    [Fact]
    public void EveryLibraryHasItsUpstreamVersionAndPublicKey()
    {
        var framework = TestAssets.FrameworkAssemblies(TestAssets.FindRepositoryRoot());
        var referencePack = UpstreamReferencePack.Directory();
        var mismatches = new List<string>();

        foreach (var path in framework)
        {
            var upstreamPath = Path.Combine(referencePack, Path.GetFileName(path));
            if (!File.Exists(upstreamPath))
            {
                mismatches.Add($"{Path.GetFileName(path)}: not in the reference pack");
                continue;
            }
            var (version, key) = Identity(path);
            var (upstreamVersion, upstreamKey) = Identity(upstreamPath);
            if (version != upstreamVersion || !key.AsSpan().SequenceEqual(upstreamKey))
            {
                mismatches.Add($"{Path.GetFileName(path)}: {version} vs {upstreamVersion}");
            }
        }

        Assert.NotEmpty(framework);
        Assert.Empty(mismatches);
    }

    // A library forwards to CoreLib each public type that upstream's assembly declares or forwards and CoreLib
    // defines, as upstream's partial facade does, so a package built against the package-era assembly (System.Memory's
    // Span<T>, say) binds through the framework's.
    [Fact]
    public void EveryLibraryForwardsTheUpstreamTypesCoreLibDefines()
    {
        var root = TestAssets.FindRepositoryRoot();
        var referencePack = UpstreamReferencePack.Directory();
        var coreLib = Defined(Path.Combine(root, "src", "NetWasm.CoreLib", "bin", "Release", "net10.0", "ref", "NetWasm.CoreLib.dll"));
        var missing = new List<string>();

        foreach (var path in TestAssets.FrameworkAssemblies(root))
        {
            var upstreamPath = Path.Combine(referencePack, Path.GetFileName(path));
            var own = Defined(path);
            var forwarded = Forwarded(path);
            var upstream = Defined(upstreamPath);
            upstream.UnionWith(Forwarded(upstreamPath).Keys);
            missing.AddRange(upstream
                .Where(type => coreLib.Contains(type) && !own.Contains(type) &&
                    !(forwarded.TryGetValue(type, out var owner) && owner == "NetWasm.CoreLib"))
                .Select(type => $"{Path.GetFileNameWithoutExtension(path)}: {type}"));
        }

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    private static HashSet<string> Defined(string path)
    {
        using var image = new PEReader(File.OpenRead(path));
        var metadata = image.GetMetadataReader();
        return [.. metadata.TypeDefinitions
            .Select(metadata.GetTypeDefinition)
            .Where(type => (type.Attributes & System.Reflection.TypeAttributes.VisibilityMask) == System.Reflection.TypeAttributes.Public)
            .Select(type => $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}")];
    }

    private static Dictionary<string, string> Forwarded(string path)
    {
        using var image = new PEReader(File.OpenRead(path));
        var metadata = image.GetMetadataReader();
        var forwarded = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var handle in metadata.ExportedTypes)
        {
            var type = metadata.GetExportedType(handle);
            if (type.Implementation.Kind == HandleKind.AssemblyReference)
            {
                forwarded[$"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}"] =
                    metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)type.Implementation).Name);
            }
        }
        return forwarded;
    }

    private static (Version Version, byte[] PublicKey) Identity(string path)
    {
        using var image = new PEReader(File.OpenRead(path));
        var metadata = image.GetMetadataReader();
        var assembly = metadata.GetAssemblyDefinition();
        return (assembly.Version, metadata.GetBlobBytes(assembly.PublicKey));
    }
}
