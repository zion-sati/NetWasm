using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

// Method signatures named as ILLink.Substitutions.xml writes them, and the stubs of instance, parameterised and void
// methods matched by them.
public sealed class SubstitutionSignatureTypeNameProviderTests
{
    private static readonly SubstitutionSignatureTypeNameProvider Provider = new();

    [Fact]
    public void ConstructedTypesAreNamedAsILLinkWritesThem()
    {
        Assert.Equal("System.Boolean", Provider.GetPrimitiveType(PrimitiveTypeCode.Boolean));
        Assert.Equal("A[]", Provider.GetSZArrayType("A"));
        Assert.Equal("A[,]", Provider.GetArrayType("A", new ArrayShape(2, [], [])));
        Assert.Equal("A&", Provider.GetByReferenceType("A"));
        Assert.Equal("A*", Provider.GetPointerType("A"));
        Assert.Equal("A", Provider.GetPinnedType("A"));
        Assert.Equal("A", Provider.GetModifiedType("M", "A", true));
        Assert.Equal("G`2<A,B>", Provider.GetGenericInstantiation("G`2", ["A", "B"]));
        Assert.Equal("!0", Provider.GetGenericTypeParameter(null, 0));
        Assert.Equal("!!1", Provider.GetGenericMethodParameter(null, 1));
        Assert.Equal("method R *(A,B)", Provider.GetFunctionPointerType(
            new MethodSignature<string>(default, "R", 0, 0, ["A", "B"])));
    }

    // Definitions and references name their namespace and their declaring types; a specification decodes.
    [Fact]
    public void DefinedReferencedAndSpecifiedTypesAreNamed()
    {
        var image = Image(out var handles);
        using var pe = new PEReader(image);
        var reader = pe.GetMetadataReader();

        Assert.Equal("Example.Outer", Provider.GetTypeFromDefinition(reader, handles.Outer, 0));
        Assert.Equal("Example.Outer/Inner", Provider.GetTypeFromDefinition(reader, handles.Inner, 0));
        Assert.Equal("Global", Provider.GetTypeFromDefinition(reader, handles.Global, 0));
        Assert.Equal("System.Diagnostics.Tracing.EventSource", Provider.GetTypeFromReference(reader, handles.Reference, 0));
        Assert.Equal("System.Diagnostics.Tracing.EventSource/Nested", Provider.GetTypeFromReference(reader, handles.NestedReference, 0));
        Assert.Equal("Example.Outer[]", Provider.GetTypeFromSpecification(reader, null, handles.Specification, 0));
        Assert.Equal("Example.Outer/Inner", new TypeDefinitionNameFormatter(Provider).Format(reader, handles.Inner));
        Assert.Equal("System.Boolean IsEnabled(System.Int32,Example.Outer/Inner)",
            new MethodSignatureNameFormatter(Provider).Format(reader, reader.GetMethodDefinition(handles.IsEnabled)));
        Assert.Equal("System.Void Finalize()", new MethodSignatureNameFormatter(Provider).Format(reader, reader.GetMethodDefinition(handles.Finalize)));
    }

    // An instance method with parameters matches its stub by signature, and a void method's stub applies whatever value
    // it carries; another overload or a Boolean stub without a value applies nowhere.
    [Fact]
    public void InstanceParameterisedAndVoidMethodsTakeTheirStubs()
    {
        var image = Image(out var handles, """
            <linker>
              <assembly fullname="Substitutions">
                <type fullname="Example.Outer" feature="Tracing" featurevalue="false">
                  <method signature="System.Boolean IsEnabled(System.Int32,Example.Outer/Inner)" body="stub" value="false" />
                  <method signature="System.Boolean IsEnabled(System.Int64)" body="stub" value="true" />
                  <method signature="System.Void Finalize()" body="stub" />
                  <method signature="System.Boolean IsEnabled()" body="stub" />
                </type>
              </assembly>
            </linker>
            """);
        using var pe = new PEReader(image);

        var stubs = FeatureSwitchSubstitutionReaderTests.Reader().Read(pe, pe.GetMetadataReader());

        Assert.Equal([new FeatureSwitchStub([("Tracing", false)], false)], stubs[MetadataTokens.GetToken(handles.IsEnabled)]);
        Assert.Equal([new FeatureSwitchStub([("Tracing", false)], false)], stubs[MetadataTokens.GetToken(handles.Finalize)]);
        Assert.Equal(2, stubs.Count);
    }

    private sealed record Handles(
        TypeDefinitionHandle Outer, TypeDefinitionHandle Inner, TypeDefinitionHandle Global, TypeReferenceHandle Reference,
        TypeReferenceHandle NestedReference, TypeSpecificationHandle Specification, MethodDefinitionHandle IsEnabled,
        MethodDefinitionHandle Finalize);

    private static ImmutableArray<byte> Image(out Handles handles, string? substitutions = null)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Substitutions.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Substitutions"), new Version(1, 0), default, default, 0, 0);
        var scope = metadata.AddAssemblyReference(metadata.GetOrAddString("System.Runtime"), new Version(11, 0), default, default, 0, default);
        var reference = metadata.AddTypeReference(scope, metadata.GetOrAddString("System.Diagnostics.Tracing"),
            metadata.GetOrAddString("EventSource"));
        var nestedReference = metadata.AddTypeReference(reference, default, metadata.GetOrAddString("Nested"));
        metadata.AddTypeDefinition(default, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

        // The inner type is defined after its methods' owner, so its handle is known for the signature below.
        var innerHandle = MetadataTokens.TypeDefinitionHandle(4);
        var isEnabled = new BlobBuilder();
        new BlobEncoder(isEnabled).MethodSignature(isInstanceMethod: true).Parameters(2, result => result.Type().Boolean(), parameters =>
        {
            parameters.AddParameter().Type().Int32();
            parameters.AddParameter().Type().Type(innerHandle, isValueType: true);
        });
        var finalize = new BlobBuilder();
        new BlobEncoder(finalize).MethodSignature(isInstanceMethod: true).Parameters(0, result => result.Void(), _ => { });
        var isEnabledMethod = metadata.AddMethodDefinition(MethodAttributes.Public, MethodImplAttributes.Runtime,
            metadata.GetOrAddString("IsEnabled"), metadata.GetOrAddBlob(isEnabled), -1, MetadataTokens.ParameterHandle(1));
        var finalizeMethod = metadata.AddMethodDefinition(MethodAttributes.Family | MethodAttributes.Virtual, MethodImplAttributes.Runtime,
            metadata.GetOrAddString("Finalize"), metadata.GetOrAddBlob(finalize), -1, MetadataTokens.ParameterHandle(1));
        var outer = metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString("Example"),
            metadata.GetOrAddString("Outer"), default, MetadataTokens.FieldDefinitionHandle(1), isEnabledMethod);
        var global = metadata.AddTypeDefinition(TypeAttributes.Public, default, metadata.GetOrAddString("Global"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(3));
        var inner = metadata.AddTypeDefinition(TypeAttributes.NestedPublic | TypeAttributes.Sealed, default,
            metadata.GetOrAddString("Inner"), default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(3));
        Assert.Equal(innerHandle, inner);
        metadata.AddNestedType(inner, outer);
        var array = new BlobBuilder();
        new BlobEncoder(array).TypeSpecificationSignature().SZArray().Type(outer, isValueType: false);
        var specification = metadata.AddTypeSpecification(metadata.GetOrAddBlob(array));

        var resources = new BlobBuilder();
        if (substitutions is not null)
        {
            metadata.AddManifestResource(ManifestResourceAttributes.Public, metadata.GetOrAddString("ILLink.Substitutions.xml"), default, 0);
            var bytes = Encoding.UTF8.GetBytes(substitutions);
            resources.WriteInt32(bytes.Length);
            resources.WriteBytes(bytes);
        }
        var image = new BlobBuilder();
        new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(), new MetadataRootBuilder(metadata), new BlobBuilder(),
            managedResources: substitutions is null ? null : resources).Serialize(image);
        handles = new(outer, inner, global, reference, nestedReference, specification, isEnabledMethod, finalizeMethod);
        return image.ToImmutableArray();
    }
}
