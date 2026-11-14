using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class FrameworkFieldContractValidatorTests
{
    [Theory]
    [InlineData(FieldAttributes.Public, FieldAttributes.Public, true)]
    [InlineData(FieldAttributes.Family, FieldAttributes.Public, true)]
    [InlineData(FieldAttributes.FamORAssem, FieldAttributes.FamORAssem, true)]
    [InlineData(FieldAttributes.Public, FieldAttributes.Family, false)]
    [InlineData(FieldAttributes.Family, FieldAttributes.Family, true)]
    [InlineData(FieldAttributes.Family, FieldAttributes.FamORAssem, true)]
    [InlineData(FieldAttributes.Family, FieldAttributes.Private, false)]
    [InlineData(FieldAttributes.FamORAssem, FieldAttributes.Public, true)]
    [InlineData(FieldAttributes.FamORAssem, FieldAttributes.Family, false)]
    [InlineData(FieldAttributes.Private, FieldAttributes.Private, false)]
    [InlineData(FieldAttributes.Assembly, FieldAttributes.Public, false)]
    [InlineData(FieldAttributes.Public, FieldAttributes.Public | FieldAttributes.Static, false)]
    [InlineData(FieldAttributes.Public, FieldAttributes.Public | FieldAttributes.InitOnly, false)]
    [InlineData(FieldAttributes.Public | FieldAttributes.InitOnly, FieldAttributes.Public, false)]
    [InlineData(FieldAttributes.Public | FieldAttributes.Literal, FieldAttributes.Public, false)]
    public void PreservesVisibilityStorageAndMutability(FieldAttributes source, FieldAttributes target, bool accepted)
    {
        using var original = Image.Create("Source", source);
        using var replacement = Image.Create("Target", target);
        Check(original, replacement, accepted);
    }

    [Theory]
    [InlineData(42, 42, true)]
    [InlineData(42, 43, false)]
    [InlineData(null, 42, false)]
    [InlineData(42, null, false)]
    public void PreservesLiteralValues(int? source, int? target, bool accepted)
    {
        const FieldAttributes flags = FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal;
        using var original = Image.Create("Source", flags, source);
        using var replacement = Image.Create("Target", flags, target);
        Check(original, replacement, accepted);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void PreservesRequiredModifiers(bool source, bool target, bool accepted)
    {
        using var original = Image.Create("Source", requiredModifier: source);
        using var replacement = Image.Create("Target", requiredModifier: target);
        Check(original, replacement, accepted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitOriginalSourceRemainsDistinctFromEqualIdentityImplementation(bool compatible)
    {
        using var original = Image.Create("Shared", FieldAttributes.Public);
        using var target = Image.Create("Shared", compatible ? FieldAttributes.Public : FieldAttributes.Private);
        var validator = Assert.IsAssignableFrom<IFrameworkFieldContractValidator>(
            new FrameworkFieldContractValidator(new Assemblies(target.Snapshot)));
        var binding = new FrameworkFieldBinding(original.Field, target.Field, "Caller.Run", 7)
        {
            OriginalSource = original.Snapshot,
        };
        if (compatible)
            Assert.Null(Record.Exception(() => validator.Validate(binding)));
        else
            Assert.Equal(DiagnosticCode.AssemblyResolution,
                Assert.Throws<CompilerException>(() => validator.Validate(binding)).Diagnostic.Code);
    }

    [Theory]
    [InlineData(FieldAttributes.Private, false)]
    [InlineData(FieldAttributes.Private, true)]
    [InlineData(FieldAttributes.Assembly, false)]
    [InlineData(FieldAttributes.Assembly, true)]
    [InlineData(FieldAttributes.FamANDAssem, false)]
    [InlineData(FieldAttributes.FamANDAssem, true)]
    public void PreservesUnchangedNonPublicContractsOnlyWithinTheAssemblyIdentity(FieldAttributes access, bool sameAssembly)
    {
        using var original = Image.Create("Source", access);
        using var target = Image.Create(sameAssembly ? "Source" : "Target", access);
        var validator = Assert.IsAssignableFrom<IFrameworkFieldContractValidator>(
            new FrameworkFieldContractValidator(new Assemblies(target.Snapshot)));
        var binding = new FrameworkFieldBinding(original.Field, target.Field, "Caller.Run", 7)
        {
            OriginalSource = original.Snapshot,
        };
        if (sameAssembly)
            Assert.Null(Record.Exception(() => validator.Validate(binding)));
        else
            Assert.Equal(DiagnosticCode.AssemblyResolution,
                Assert.Throws<CompilerException>(() => validator.Validate(binding)).Diagnostic.Code);
    }

    private static void Check(Image original, Image target, bool accepted)
    {
        var validator = Assert.IsAssignableFrom<IFrameworkFieldContractValidator>(
            new FrameworkFieldContractValidator(new Assemblies(original.Snapshot, target.Snapshot)));
        var binding = new FrameworkFieldBinding(original.Field, target.Field, "Caller.Run", 7);
        if (accepted)
        {
            Assert.Null(Record.Exception(() => validator.Validate(binding)));
            return;
        }
        var error = Assert.Throws<CompilerException>(() => validator.Validate(binding));
        Assert.Equal(DiagnosticCode.AssemblyResolution, error.Diagnostic.Code);
        Assert.Contains("Source::Value", error.Message);
        Assert.Contains("Target::Value", error.Message);
    }

    private sealed class Assemblies(params MetadataAssemblySnapshot[] values) : IMetadataAssemblyResolver
    {
        public MetadataAssemblySnapshot Resolve(AssemblyIdentity identity) => values.Single(v => v.Identity == identity);
    }

    private sealed class Image(MetadataReaderProvider provider, MetadataAssemblySnapshot snapshot,
        FieldDefinitionModel fieldModel) : IDisposable
    {
        public MetadataAssemblySnapshot Snapshot => snapshot;
        public FieldDefinitionModel Field => fieldModel;
        public void Dispose() => provider.Dispose();

        public static Image Create(string name, FieldAttributes attributes = FieldAttributes.Public,
            int? constant = null, bool requiredModifier = false)
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(0, metadata.GetOrAddString(name), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
            var contract = metadata.AddAssemblyReference(metadata.GetOrAddString("Contract"), new Version(1, 0),
                default, default, 0, default);
            var modifier = metadata.AddTypeReference(contract, metadata.GetOrAddString("Example"),
                metadata.GetOrAddString("Modifier"));
            var signature = new BlobBuilder();
            signature.WriteByte(0x06);
            if (requiredModifier)
            {
                signature.WriteByte(0x1f);
                signature.WriteCompressedInteger(CodedIndex.TypeDefOrRef(modifier));
            }
            signature.WriteByte(0x08);
            var fieldHandle = metadata.AddFieldDefinition(attributes, metadata.GetOrAddString("Value"),
                metadata.GetOrAddBlob(signature));
            if (constant.HasValue) metadata.AddConstant(fieldHandle, constant.Value);
            var bytes = new BlobBuilder();
            new MetadataRootBuilder(metadata).Serialize(bytes, 0, 0);
            var reader = MetadataReaderProvider.FromMetadataImage(bytes.ToImmutableArray());
            var identity = new AssemblyIdentity(name);
            var field = new FieldDefinitionModel(new(identity, MetadataTokens.GetToken(fieldHandle)),
                new(identity, 0x02000001), "Value", CliValueKind.I4, (attributes & FieldAttributes.Static) != 0);
            var snapshot = new MetadataAssemblySnapshot(identity, reader.GetMetadataReader(),
                ImmutableDictionary<int, TypeDefinitionModel>.Empty,
                ImmutableDictionary<int, FieldDefinitionModel>.Empty.Add(field.Key.MetadataToken, field),
                ImmutableDictionary<int, MethodDefinitionModel>.Empty, ImmutableDictionary<int, EntityHandle>.Empty);
            return new Image(reader, snapshot, field);
        }
    }
}
