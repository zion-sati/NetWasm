using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NetWasm.Sdk.Pack.References;
using Xunit;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class StandardFacadeBuilderTests
{
    [Fact]
    public void ForwardsOnlyContractTypesProvidedByPublicOwners()
    {
        var contract = Assembly("netstandard", "System.Object", "System.Missing");
        var core = Assembly("NetWasm.CoreLib", "System.Object", "System.Extra", "!System.Missing");
        using var pe = Open(new StandardFacadeBuilder().Build(contract, [core]));
        var reader = pe.GetMetadataReader();
        var exported = reader.GetExportedType(Assert.Single(reader.ExportedTypes));
        Assert.Equal("Object", reader.GetString(exported.Name));
        Assert.Equal("System", reader.GetString(exported.Namespace));
        Assert.Equal("NetWasm.CoreLib", reader.GetString(reader.GetAssemblyReference(
            (AssemblyReferenceHandle)exported.Implementation).Name));
        Assert.Empty(reader.MethodDefinitions);
        Assert.Single(reader.TypeDefinitions); // Only <Module>, never executable stubs.
        Assert.Equal(0, pe.PEHeaders.CorHeader!.EntryPointTokenOrRelativeVirtualAddress);
    }

    [Fact]
    public void NestedTypesRetainTheirExportedDeclaringTypeChain()
    {
        string[] types = ["Example.Outer`1", "Example.Outer`1+Inner", "Example.Outer`1+Inner+Leaf"];
        using var pe = Open(new StandardFacadeBuilder().Build(
            Assembly("netstandard", types), [Assembly("Owner", types)]));
        var reader = pe.GetMetadataReader();
        var exports = reader.ExportedTypes.ToArray();
        Assert.Equal(3, exports.Length);
        Assert.Equal(HandleKind.AssemblyReference, reader.GetExportedType(exports[0]).Implementation.Kind);
        Assert.Equal(exports[0], reader.GetExportedType(exports[1]).Implementation);
        Assert.Equal(exports[1], reader.GetExportedType(exports[2]).Implementation);
    }

    [Fact]
    public void OptionalOwnerIsOnlyAnAssemblyReferenceNotAnImplementation()
    {
        var contract = Assembly("netstandard", "System.Object", "System.Linq.Enumerable");
        using var pe = Open(new StandardFacadeBuilder().Build(contract,
            [Assembly("NetWasm.CoreLib", "System.Object"), Assembly("System.Linq", "System.Linq.Enumerable")]));
        var reader = pe.GetMetadataReader();
        Assert.Equal(["NetWasm.CoreLib", "System.Linq"], reader.AssemblyReferences
            .Select(x => reader.GetString(reader.GetAssemblyReference(x).Name)).Order().ToArray());
        Assert.Empty(reader.MethodDefinitions);
        Assert.Empty(reader.AssemblyFiles);
    }

    [Fact]
    public void OutputIsDeterministicRegardlessOfOwnerInputOrder()
    {
        var contract = Assembly("netstandard", "System.Object", "System.Linq.Enumerable");
        var core = Assembly("NetWasm.CoreLib", "System.Object");
        var linq = Assembly("System.Linq", "System.Linq.Enumerable");
        var builder = new StandardFacadeBuilder();
        Assert.Equal(builder.Build(contract, [core, linq]), builder.Build(contract, [linq, core]));
    }

    [Fact]
    public void ChangedOwnerContentChangesModuleIdentity()
    {
        var contract = Assembly("netstandard", "System.Object");
        var builder = new StandardFacadeBuilder();
        using var before = Open(builder.Build(contract, [Assembly("Owner", "System.Object")]));
        using var after = Open(builder.Build(contract, [Assembly("Owner", "System.Object", "System.Extra")]));
        var first = before.GetMetadataReader();
        var second = after.GetMetadataReader();
        Assert.NotEqual(first.GetGuid(first.GetModuleDefinition().Mvid), second.GetGuid(second.GetModuleDefinition().Mvid));
    }

    [Fact]
    public void ContractIdentityAndOwnerVersionArePreserved()
    {
        var key = typeof(object).Assembly.GetName().GetPublicKey()!;
        var contract = AssemblyImage("netstandard", new Version(2, 1, 0, 0), key, "System.Object");
        var owner = AssemblyImage("Owner", new Version(3, 4, 5, 6), key, "System.Object");
        using var pe = Open(new StandardFacadeBuilder().Build(contract, [owner]));
        var reader = pe.GetMetadataReader();
        var definition = reader.GetAssemblyDefinition();
        Assert.Equal("netstandard", reader.GetString(definition.Name));
        Assert.Equal(new Version(2, 1, 0, 0), definition.Version);
        Assert.Equal(key, reader.GetBlobBytes(definition.PublicKey));
        Assert.Equal(AssemblyFlags.PublicKey, definition.Flags);
        var reference = reader.GetAssemblyReference(Assert.Single(reader.AssemblyReferences));
        Assert.Equal(new Version(3, 4, 5, 6), reference.Version);
        Assert.Equal(key, reader.GetBlobBytes(reference.PublicKeyOrToken));
    }

    [Fact]
    public void AmbiguousTypeOwnersAreRejected()
    {
        var contract = Assembly("netstandard", "System.Object");
        Assert.Throws<ArgumentException>(() => new StandardFacadeBuilder().Build(contract,
            [Assembly("First", "System.Object"), Assembly("Second", "System.Object")]));
    }

    [Fact]
    public void DuplicateOrSelfOwnerIdentitiesAreRejected()
    {
        var contract = Assembly("netstandard", "System.Object");
        Assert.Throws<ArgumentException>(() => new StandardFacadeBuilder().Build(contract, [contract]));
        var owner = Assembly("Owner", "System.Object");
        Assert.Throws<ArgumentException>(() => new StandardFacadeBuilder().Build(contract, [owner, owner]));
    }

    [Fact]
    public void NonStandardContractIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new StandardFacadeBuilder().Build(Assembly("Other"), []));
    }

    [Fact]
    public void NullAndMalformedImagesAreRejected()
    {
        var builder = new StandardFacadeBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.Build(null!, []));
        Assert.Throws<ArgumentNullException>(() => builder.Build(Assembly("netstandard"), null!));
        Assert.Throws<BadImageFormatException>(() => builder.Build([1, 2, 3], []));
        Assert.Throws<ArgumentNullException>(() => builder.Build(Assembly("netstandard"), [null!]));
    }

    [Fact]
    public void AnyContractGetsAFacadeWithItsIdentityAndCountsTheTypesItForwards()
    {
        var contract = Assembly("System.Runtime", "System.Object", "System.ArgIterator");
        var facade = new StandardFacadeBuilder().BuildFacade(contract, [Assembly("NetWasm.CoreLib", "System.Object")]);

        Assert.Equal(("System.Runtime", 1, 2), (facade.Name, facade.Forwarded, facade.ContractTypes));
        using var pe = Open(facade.Image);
        var reader = pe.GetMetadataReader();
        Assert.Equal("System.Runtime", reader.GetString(reader.GetAssemblyDefinition().Name));
        Assert.Equal("System.Runtime.dll", reader.GetString(reader.GetModuleDefinition().Name));
        Assert.Equal(["System.Object"], reader.ExportedTypes.Select(handle => reader.GetExportedType(handle))
            .Select(type => reader.GetString(type.Namespace) + "." + reader.GetString(type.Name)));
    }

    [Fact]
    public void AFacadeContractsForwardersAreItsTypes()
    {
        // .NET 11's System.Buffers forwards ArrayPool<T> and its nested types to System.Runtime.
        var contract = Forwarding("System.Buffers", "System.Runtime", "System.Buffers.ArrayPool`1", "System.Buffers.ArrayPool`1+Nested");
        var facade = new StandardFacadeBuilder().BuildFacade(contract,
            [Assembly("NetWasm.CoreLib", "System.Buffers.ArrayPool`1", "System.Buffers.ArrayPool`1+Nested")]);

        Assert.Equal(("System.Buffers", 2, 2), (facade.Name, facade.Forwarded, facade.ContractTypes));
    }

    [Fact]
    public void AnOwnersForwardersDoNotMakeItAnOwner()
    {
        // NetWasm.App's System.Collections forwards ConcurrentDictionary<,> to System.Collections.Concurrent.
        var contract = Assembly("System.Runtime", "System.Collections.Concurrent.ConcurrentDictionary`2");
        var facade = new StandardFacadeBuilder().BuildFacade(contract,
            [Assembly("System.Collections.Concurrent", "System.Collections.Concurrent.ConcurrentDictionary`2"),
             Forwarding("System.Collections", "System.Collections.Concurrent", "System.Collections.Concurrent.ConcurrentDictionary`2")]);

        Assert.Equal(1, facade.Forwarded);
    }

    private static byte[] Forwarding(string name, string target, params string[] types)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString(name + ".dll"), metadata.GetOrAddGuid(Guid.Empty), default, default);
        metadata.AddAssembly(metadata.GetOrAddString(name), new Version(11, 0, 0, 0), default, default, 0,
            AssemblyHashAlgorithm.Sha256);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var reference = metadata.AddAssemblyReference(metadata.GetOrAddString(target), new Version(11, 0, 0, 0), default,
            default, 0, default);
        var exported = new Dictionary<string, ExportedTypeHandle>();
        foreach (var fullName in types)
        {
            var nested = fullName.LastIndexOf('+');
            var separator = nested < 0 ? fullName.LastIndexOf('.') : nested;
            exported[fullName] = metadata.AddExportedType(
                nested < 0 ? TypeAttributes.Public | (TypeAttributes)0x00200000 : TypeAttributes.NestedPublic,
                metadata.GetOrAddString(nested < 0 ? fullName[..separator] : ""),
                metadata.GetOrAddString(fullName[(separator + 1)..]),
                nested < 0 ? reference : exported[fullName[..nested]], 0);
        }
        var pe = new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics:
            Characteristics.ExecutableImage | Characteristics.Dll),
            new MetadataRootBuilder(metadata), new BlobBuilder(), flags: CorFlags.ILOnly);
        var output = new BlobBuilder();
        pe.Serialize(output);
        return output.ToArray();
    }

    private static PEReader Open(byte[] image) => new(new MemoryStream(image));

    internal static byte[] Assembly(string name, params string[] types) =>
        AssemblyImage(name, new Version(1, 0, 0, 0), [], types);

    private static byte[] AssemblyImage(string name, Version version, byte[] key, params string[] types)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString(name + ".dll"),
            metadata.GetOrAddGuid(Guid.Empty), default, default);
        metadata.AddAssembly(metadata.GetOrAddString(name), version, default,
            metadata.GetOrAddBlob(key), key.Length == 0 ? 0 : AssemblyFlags.PublicKey, AssemblyHashAlgorithm.Sha256);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var handles = new Dictionary<string, TypeDefinitionHandle>();
        foreach (var text in types)
        {
            var fullName = text.TrimStart('!');
            var nested = fullName.LastIndexOf('+');
            var separator = nested < 0 ? fullName.LastIndexOf('.') : nested;
            var ns = nested < 0 && separator >= 0 ? fullName[..separator] : "";
            var attributes = text.StartsWith('!') ? TypeAttributes.NotPublic :
                nested < 0 ? TypeAttributes.Public : TypeAttributes.NestedPublic;
            var handle = metadata.AddTypeDefinition(attributes, metadata.GetOrAddString(ns),
                metadata.GetOrAddString(fullName[(separator + 1)..]), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            handles.Add(fullName, handle);
            if (nested >= 0) metadata.AddNestedType(handle, handles[fullName[..nested]]);
        }
        var pe = new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics:
            Characteristics.ExecutableImage | Characteristics.Dll),
            new MetadataRootBuilder(metadata), new BlobBuilder(), flags: CorFlags.ILOnly);
        var output = new BlobBuilder();
        pe.Serialize(output);
        return output.ToArray();
    }
}
