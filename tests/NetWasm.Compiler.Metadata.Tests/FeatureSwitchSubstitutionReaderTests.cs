using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

// An assembly's embedded ILLink.Substitutions.xml stubs its static, parameterless Boolean methods.
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
        return (FeatureSwitchSubstitutionReader.Read(pe, pe.GetMetadataReader()), MetadataTokens.GetToken(method));
    }
}
