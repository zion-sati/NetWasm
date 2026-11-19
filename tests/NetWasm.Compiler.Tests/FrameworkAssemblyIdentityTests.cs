// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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

    // A package built for .NET 5 or later names each type by the assembly .NET's reference pack declares it in, and one
    // built against an older contract by the assembly that declared it then, which the pack's assembly of that name now
    // forwards. Every such type NetWasm defines, in CoreLib or a NetWasm.App library, binds from that name: the library
    // of that name defines or forwards it, or, where NetWasm.App ships no such library, NetWasm.App.Ref generates a
    // forwarding facade. The generator makes one for a contract whose types NetWasm defines in full, or in part when
    // NetWasm.App.Ref lists the contract as one whose missing types no consumer can need. A partial contract that also
    // ships as a package keeps the package's implementation and gets no facade.
    [Fact]
    public void EveryUpstreamTypeNetWasmDefinesBindsFromItsUpstreamAssembly()
    {
        var root = TestAssets.FindRepositoryRoot();
        var referencePack = UpstreamReferencePack.Directory();
        var framework = TestAssets.FrameworkAssemblies(root)
            .ToDictionary(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase);
        var owners = framework.Values
            .Append(Path.Combine(root, "src", "NetWasm.CoreLib", "bin", "Release", "net10.0", "ref", "NetWasm.CoreLib.dll"))
            .SelectMany(Defined)
            .ToHashSet(StringComparer.Ordinal);
        var partialFacades = PartialFacadeContracts(Path.Combine(root, "src", "NetWasm.App.Ref", "NetWasm.App.Ref.csproj"));
        var unbound = new List<string>();

        foreach (var contract in System.IO.Directory.GetFiles(referencePack, "*.dll").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(contract);
            if (name == "netstandard")
            {
                continue;
            }
            var types = Defined(contract);
            types.UnionWith(Forwarded(contract).Keys);
            var owned = types.Where(owners.Contains).ToArray();
            if (framework.TryGetValue(name, out var library))
            {
                var own = Defined(library);
                var forwarded = Forwarded(library);
                unbound.AddRange(owned
                    .Where(type => !own.Contains(type) && !forwarded.ContainsKey(type))
                    .Select(type => $"{name}: {type} (NetWasm defines it in another assembly)"));
            }
            else if (owned.Length != 0 && owned.Length < types.Count && !partialFacades.Contains(name) &&
                !PackageContracts.Contains(name))
            {
                unbound.Add($"{name}: no facade for its {owned.Length} types NetWasm defines (it defines {owned.Length} of {types.Count})");
            }
        }

        Assert.True(unbound.Count == 0, string.Join(Environment.NewLine, unbound));
    }

    // Partial contracts that also ship as NuGet packages, whose implementations a framework facade would replace.
    private static readonly HashSet<string> PackageContracts = new(StringComparer.Ordinal)
    {
        "Microsoft.Extensions.Caching.Abstractions",
        "System.Reflection.TypeExtensions",
        "System.Security.Cryptography.Cng",
    };

    private static HashSet<string> PartialFacadeContracts(string project)
    {
        var document = System.Xml.Linq.XDocument.Load(project);
        return [.. document.Descendants("NetWasmLegacyFacadeContract")
            .SelectMany(item => ((string?)item.Attribute("Include") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))];
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
