using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

// An assembly's embedded ILLink.Substitutions.xml stubs its Boolean and void methods (SubstitutionSignatureTypeNameProviderTests
// covers instance, parameterised and void ones).
public sealed class FeatureSwitchSubstitutionReaderTests
{
    // Elements without a type name, a stub body or a readable feature value apply nowhere; the rest apply.
    [Fact]
    public void OnlyCompleteStubsWithReadableConditionsApply()
    {
        const string xml = """
            <linker>
              <assembly fullname="Switches">
                <type>
                  <method signature="System.Boolean Enabled()" body="stub" value="true" feature="Unnamed" featurevalue="true" />
                </type>
                <type fullname="Example.Switches">
                  <method signature="System.Boolean Enabled()" value="true" feature="NoBody" featurevalue="true" />
                  <method signature="System.Boolean Enabled()" body="stub" value="false" feature="NoValue" />
                  <method signature="System.Boolean Enabled()" body="stub" value="true" feature="Kept" featurevalue="false" />
                  <method signature="System.Boolean Enabled()" body="stub" value="maybe" feature="NoResult" featurevalue="true" />
                  <method body="stub" value="true" feature="NoSignature" featurevalue="true" />
                  <method signature="System.Boolean Enabled()" body="stub" feature="NoResultAttribute" featurevalue="true" />
                  <method signature="System.Int32 Enabled()" body="stub" value="true" feature="NotBoolean" featurevalue="true" />
                  <method signature="System.Boolean Enabled(System.Int32)" body="stub" value="true" feature="Parameters" featurevalue="true" />
                  <method signature="System.Boolean Enabled()" body="stub" value="true" />
                  <method signature="System.Boolean Enabled()" body="stub" value="false" feature="Second" featurevalue="true" />
                </type>
                <type fullname="Example.Missing" />
                <type fullname="Example.Switches/Inner" feature="Nested" featurevalue="true">
                  <method signature="System.Boolean Enabled()" body="stub" value="true" />
                </type>
              </assembly>
            </linker>
            """;
        var (stubs, method) = Read(xml);

        // The first method, Other, has another name; the nested type has no methods of its own.
        Assert.Equal([new FeatureSwitchStub([("Kept", false)], true), new FeatureSwitchStub([("Second", true)], false)],
            Assert.Single(stubs, entry => entry.Key == method).Value);
        Assert.Single(stubs);
    }

    [Fact]
    public void AnAssemblyWithoutTheResourceHasNoStubs() => Assert.Empty(Read(null).Stubs);

    // A substitutions resource that is not well-formed XML, or whose image has no resource data, applies nothing.
    [Theory]
    [InlineData("<linker>", true)]
    [InlineData("<linker />", false)]
    public void AnUnreadableResourceAppliesNothing(string xml, bool withData) =>
        Assert.Empty(Read(xml, withData).Stubs);

    private static (Dictionary<int, System.Collections.Immutable.ImmutableArray<FeatureSwitchStub>> Stubs, int Method) Read(
        string? xml, bool withData = true)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Switches.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Switches"), new Version(1, 0), default, default, 0, 0);
        metadata.AddTypeDefinition(default, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(0, result => result.Type().Boolean(), _ => { });
        var other = metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static, MethodImplAttributes.Runtime,
            metadata.GetOrAddString("Other"), metadata.GetOrAddBlob(signature), -1, MetadataTokens.ParameterHandle(1));
        var method = metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static, MethodImplAttributes.Runtime,
            metadata.GetOrAddString("Enabled"), metadata.GetOrAddBlob(signature), -1, MetadataTokens.ParameterHandle(1));
        var outer = metadata.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            metadata.GetOrAddString("Example"), metadata.GetOrAddString("Switches"), default,
            MetadataTokens.FieldDefinitionHandle(1), other);
        var inner = metadata.AddTypeDefinition(TypeAttributes.NestedPublic, default, metadata.GetOrAddString("Inner"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(3));
        metadata.AddNestedType(inner, outer);
        var resources = new BlobBuilder();
        // Another resource comes first; the substitutions follow it.
        metadata.AddManifestResource(ManifestResourceAttributes.Public, metadata.GetOrAddString("Other.txt"), default, 0);
        resources.WriteInt32(0);
        if (xml is not null)
        {
            metadata.AddManifestResource(ManifestResourceAttributes.Public, metadata.GetOrAddString("ILLink.Substitutions.xml"),
                default, (uint)resources.Count);
            var bytes = Encoding.UTF8.GetBytes(xml);
            resources.WriteInt32(bytes.Length);
            resources.WriteBytes(bytes);
        }
        var image = new BlobBuilder();
        new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(), new MetadataRootBuilder(metadata), new BlobBuilder(),
            managedResources: withData ? resources : null).Serialize(image);
        using var pe = new PEReader(image.ToImmutableArray());
        return (Reader().Read(pe, pe.GetMetadataReader()), MetadataTokens.GetToken(method));
    }

    // The reader as the compiler composes it: one naming Strategy for types and signatures.
    internal static FeatureSwitchSubstitutionReader Reader()
    {
        var names = new SubstitutionSignatureTypeNameProvider();
        return new(new EmbeddedResourceReader(), new TypeDefinitionNameFormatter(names), new MethodSignatureNameFormatter(names));
    }

    [Fact]
    public void TheReaderRejectsMissingCollaboratorsAndInputs()
    {
        var names = new SubstitutionSignatureTypeNameProvider();
        Assert.Throws<ArgumentNullException>(() => new FeatureSwitchSubstitutionReader(null!, new TypeDefinitionNameFormatter(names),
            new MethodSignatureNameFormatter(names)));
        Assert.Throws<ArgumentNullException>(() => new FeatureSwitchSubstitutionReader(new EmbeddedResourceReader(), null!,
            new MethodSignatureNameFormatter(names)));
        Assert.Throws<ArgumentNullException>(() => new FeatureSwitchSubstitutionReader(new EmbeddedResourceReader(),
            new TypeDefinitionNameFormatter(names), null!));
        Assert.Throws<ArgumentNullException>(() => new TypeDefinitionNameFormatter(null!));
        Assert.Throws<ArgumentNullException>(() => new MethodSignatureNameFormatter(null!));
        Assert.Throws<ArgumentNullException>(() => new TypeDefinitionNameFormatter(names).Format(null!, default));
        Assert.Throws<ArgumentNullException>(() => new MethodSignatureNameFormatter(names).Format(null!, default));
        using var pe = new PEReader(Image());
        Assert.Throws<ArgumentNullException>(() => Reader().Read(null!, pe.GetMetadataReader()));
        Assert.Throws<ArgumentNullException>(() => Reader().Read(pe, null!));
        Assert.Throws<ArgumentNullException>(() => new EmbeddedResourceReader().Read(null!, pe.GetMetadataReader(), "x"));
        Assert.Throws<ArgumentNullException>(() => new EmbeddedResourceReader().Read(pe, null!, "x"));
        Assert.Throws<ArgumentNullException>(() => new EmbeddedResourceReader().Read(pe, pe.GetMetadataReader(), null!));
    }

    // A resource another file holds is not embedded, so it has no bytes here; an embedded one reads back exactly.
    [Fact]
    public void OnlyEmbeddedResourcesHaveBytes()
    {
        using var pe = new PEReader(Image());
        var reader = pe.GetMetadataReader();

        Assert.Null(new EmbeddedResourceReader().Read(pe, reader, "Linked.txt"));
        Assert.Null(new EmbeddedResourceReader().Read(pe, reader, "Missing.txt"));
        Assert.Equal([1, 2, 3], new EmbeddedResourceReader().Read(pe, reader, "Embedded.txt"));
    }

    private static ImmutableArray<byte> Image()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Resources.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Resources"), new Version(1, 0), default, default, 0, 0);
        metadata.AddTypeDefinition(default, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var file = metadata.AddAssemblyFile(metadata.GetOrAddString("Linked.txt"), default, containsMetadata: false);
        metadata.AddManifestResource(ManifestResourceAttributes.Public, metadata.GetOrAddString("Linked.txt"), file, 0);
        metadata.AddManifestResource(ManifestResourceAttributes.Public, metadata.GetOrAddString("Embedded.txt"), default, 0);
        var resources = new BlobBuilder();
        resources.WriteInt32(3);
        resources.WriteBytes(new byte[] { 1, 2, 3 });
        var image = new BlobBuilder();
        new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(), new MetadataRootBuilder(metadata), new BlobBuilder(),
            managedResources: resources).Serialize(image);
        return image.ToImmutableArray();
    }
}
