using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataCrossAssemblyPrivateAccessValidatorTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, false, 1)]
    [InlineData(false, false, 2)]
    [InlineData(false, false, 3)]
    [InlineData(false, false, 4)]
    [InlineData(false, false, 5)]
    [InlineData(false, false, 6)]
    [InlineData(false, true, 0)]
    [InlineData(false, true, 1)]
    [InlineData(false, true, 2)]
    [InlineData(false, true, 3)]
    [InlineData(false, true, 4)]
    [InlineData(false, true, 5)]
    [InlineData(false, true, 6)]
    [InlineData(true, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, false, 2)]
    [InlineData(true, false, 3)]
    [InlineData(true, false, 4)]
    [InlineData(true, false, 5)]
    [InlineData(true, false, 6)]
    [InlineData(true, true, 0)]
    [InlineData(true, true, 1)]
    [InlineData(true, true, 2)]
    [InlineData(true, true, 3)]
    [InlineData(true, true, 4)]
    [InlineData(true, true, 5)]
    [InlineData(true, true, 6)]
    public void RejectsOnlyTheCrossAssemblyPrivateViolation(bool field, bool sameAssembly, int access)
    {
        var builder = new MetadataBuilder();
        builder.AddModule(0, builder.GetOrAddString("Target.dll"),
            builder.GetOrAddGuid(Guid.NewGuid()), default, default);
        var method = builder.AddMethodDefinition((MethodAttributes)access, MethodImplAttributes.IL,
            builder.GetOrAddString("Read"), default, 0, default);
        var memberField = builder.AddFieldDefinition((FieldAttributes)access,
            builder.GetOrAddString("Value"), default);
        var image = new BlobBuilder();
        new MetadataRootBuilder(builder).Serialize(image, 0, 0);
        using var provider = MetadataReaderProvider.FromMetadataImage(image.ToImmutableArray());
        var assembly = new AssemblyIdentity("Target");
        var source = new MetadataAssemblySnapshot(assembly, provider.GetMetadataReader(),
            ImmutableDictionary<int, TypeDefinitionModel>.Empty,
            ImmutableDictionary<int, FieldDefinitionModel>.Empty,
            ImmutableDictionary<int, MethodDefinitionModel>.Empty,
            ImmutableDictionary<int, EntityHandle>.Empty);
        var sources = new Sources(source);
        var validator = Assert.IsAssignableFrom<IMetadataCrossAssemblyPrivateAccessValidator>(
            new MetadataCrossAssemblyPrivateAccessValidator(sources));
        var request = new MetadataMemberAccessRequest(sameAssembly ? assembly : new("Caller"),
            new(assembly, field ? MetadataTokens.GetToken(memberField) : MetadataTokens.GetToken(method)), "Caller.Read", 17);

        if (!sameAssembly && access <= 1)
        {
            var failure = Assert.Throws<CompilerException>(() => validator.Validate(request));
            Assert.Equal(DiagnosticCode.AssemblyResolution, failure.Diagnostic.Code);
            Assert.Equal(request.Caller, failure.Diagnostic.Method);
            Assert.Equal(request.IlOffset, failure.Diagnostic.IlOffset);
        }
        else
            Assert.Null(Record.Exception(() => validator.Validate(request)));
        Assert.Equal(sameAssembly ? 0 : 1, sources.Calls);
    }

    [Fact]
    public void RejectsInvalidRequests()
    {
        Assert.Throws<ArgumentNullException>(() => new MetadataCrossAssemblyPrivateAccessValidator(null!));
        var sources = new Sources(new(new("Target"), null!,
            ImmutableDictionary<int, TypeDefinitionModel>.Empty,
            ImmutableDictionary<int, FieldDefinitionModel>.Empty,
            ImmutableDictionary<int, MethodDefinitionModel>.Empty,
            ImmutableDictionary<int, EntityHandle>.Empty));
        var validator = Assert.IsAssignableFrom<IMetadataCrossAssemblyPrivateAccessValidator>(
            new MetadataCrossAssemblyPrivateAccessValidator(sources));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));
        Assert.Throws<ArgumentException>(() => validator.Validate(new(new("Caller"),
            new(new("Target"), 0x02000001), "Caller.Read", 0)));
    }

    private sealed class Sources(MetadataAssemblySnapshot source) : IMetadataAssemblyResolver
    {
        public int Calls { get; private set; }
        public MetadataAssemblySnapshot Resolve(AssemblyIdentity identity)
        {
            Assert.Equal(source.Identity, identity);
            Calls++;
            return source;
        }
    }
}
