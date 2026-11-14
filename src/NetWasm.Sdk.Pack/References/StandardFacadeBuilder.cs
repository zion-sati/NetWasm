using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace NetWasm.Sdk.Pack.References;

/// <summary>
/// Producer-side strategy: intersects the standard contract with actual public
/// definitions and emits forwarders only. No target assembly is loaded or executed.
/// </summary>
public sealed class StandardFacadeBuilder : IStandardFacadeBuilder
{
    private const TypeAttributes Forwarder = (TypeAttributes)0x00200000;

    public byte[] Build(byte[] contract, IReadOnlyList<byte[]> owners)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (Read(contract).Name != "netstandard")
            throw new ArgumentException("The facade contract must be the netstandard assembly.", nameof(contract));
        return BuildFacade(contract, owners).Image;
    }

    public FacadeImage BuildFacade(byte[] contract, IReadOnlyList<byte[]> owners)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(owners);
        var standard = Read(contract, forwarders: true);

        var destinations = new SortedDictionary<string, AssemblyContract>(StringComparer.Ordinal);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var image in owners)
        {
            var owner = Read(image);
            if (owner.Name == standard.Name || !identities.Add(owner.Name))
                throw new ArgumentException("Facade owners must have distinct non-contract assembly identities.", nameof(owners));
            foreach (var name in owner.Types)
                if (standard.Types.Contains(name) && !destinations.TryAdd(name, owner))
                    throw new ArgumentException($"Multiple facade owners define '{name}'.", nameof(owners));
        }

        var metadata = new MetadataBuilder();
        // Hash content, not paths or input enumeration order. The facade's own
        // identity matches the standard contract; this is not an upstream signature.
        using var identity = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        identity.AppendData(contract);
        foreach (var owner in destinations.Values.Distinct().OrderBy(x => x.Name, StringComparer.Ordinal))
            identity.AppendData(owner.Hash);
        var mvid = new Guid(identity.GetHashAndReset().AsSpan(0, 16));
        metadata.AddModule(0, metadata.GetOrAddString(standard.Name + ".dll"), metadata.GetOrAddGuid(mvid), default, default);
        metadata.AddAssembly(metadata.GetOrAddString(standard.Name), standard.Version,
            metadata.GetOrAddString(standard.Culture), metadata.GetOrAddBlob(standard.PublicKey),
            standard.Flags, AssemblyHashAlgorithm.Sha256);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default,
            metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

        var references = new Dictionary<string, AssemblyReferenceHandle>(StringComparer.Ordinal);
        var exports = new Dictionary<string, ExportedTypeHandle>(StringComparer.Ordinal);
        foreach (var (fullName, owner) in destinations)
        {
            EntityHandle implementation;
            string name;
            string ns;
            var nested = fullName.LastIndexOf('+');
            if (nested >= 0)
            {
                if (!exports.TryGetValue(fullName[..nested], out var parent) ||
                    destinations[fullName[..nested]].Name != owner.Name)
                    throw new ArgumentException($"Nested facade type '{fullName}' has no matching declaring owner.", nameof(owners));
                implementation = parent;
                ns = "";
                name = fullName[(nested + 1)..];
            }
            else
            {
                if (!references.TryGetValue(owner.Name, out var reference))
                {
                    reference = metadata.AddAssemblyReference(metadata.GetOrAddString(owner.Name),
                        owner.Version, metadata.GetOrAddString(owner.Culture),
                        metadata.GetOrAddBlob(owner.PublicKey), owner.Flags, default);
                    references.Add(owner.Name, reference);
                }
                implementation = reference;
                var separator = fullName.LastIndexOf('.');
                ns = separator < 0 ? "" : fullName[..separator];
                name = fullName[(separator + 1)..];
            }
            var attributes = nested < 0 ? TypeAttributes.Public | Forwarder : TypeAttributes.NestedPublic;
            exports.Add(fullName, metadata.AddExportedType(attributes,
                metadata.GetOrAddString(ns), metadata.GetOrAddString(name), implementation, 0));
        }

        var pe = new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics:
                Characteristics.ExecutableImage | Characteristics.Dll),
            new MetadataRootBuilder(metadata), new BlobBuilder(),
            flags: CorFlags.ILOnly,
            deterministicIdProvider: blobs => BlobContentId.FromHash(Hash(blobs)));
        var output = new BlobBuilder();
        pe.Serialize(output);
        return new FacadeImage(standard.Name, output.ToArray(), destinations.Count, standard.Types.Count);
    }

    private static ImmutableArray<byte> Hash(IEnumerable<Blob> blobs)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var blob in blobs) hash.AppendData(blob.GetBytes());
        return ImmutableArray.Create(hash.GetHashAndReset());
    }

    // A contract's types include its forwarders; an owner's are those it defines.
    private static AssemblyContract Read(byte[] image, bool forwarders = false)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var assembly = metadata.GetAssemblyDefinition();
        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handle in metadata.TypeDefinitions)
            if (PublicName(metadata, handle) is { } name) types.Add(name);
        // A reference pack's facade contract (System.Buffers forwards to System.Runtime) names its types by forwarders.
        if (forwarders)
            foreach (var handle in metadata.ExportedTypes)
                if (ExportedName(metadata, handle) is { } name) types.Add(name);
        return new(metadata.GetString(assembly.Name), assembly.Version,
            metadata.GetString(assembly.Culture), metadata.GetBlobBytes(assembly.PublicKey),
            assembly.Flags & AssemblyFlags.PublicKey, types, SHA256.HashData(image));
    }

    private static string? PublicName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var visibility = type.Attributes & TypeAttributes.VisibilityMask;
        var name = reader.GetString(type.Name);
        if (visibility == TypeAttributes.Public)
        {
            var ns = reader.GetString(type.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }
        if (visibility is TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem)
        {
            var parent = type.GetDeclaringType();
            if (!parent.IsNil && PublicName(reader, parent) is { } enclosing) return enclosing + "+" + name;
        }
        return null;
    }

    private static string? ExportedName(MetadataReader reader, ExportedTypeHandle handle)
    {
        var type = reader.GetExportedType(handle);
        var name = reader.GetString(type.Name);
        if (type.Implementation.Kind == HandleKind.ExportedType)
            return ExportedName(reader, (ExportedTypeHandle)type.Implementation) is { } enclosing ? enclosing + "+" + name : null;
        if (!type.IsForwarder) return null;
        var ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private sealed record AssemblyContract(string Name, Version Version, string Culture,
        byte[] PublicKey, AssemblyFlags Flags, HashSet<string> Types, byte[] Hash);
}
