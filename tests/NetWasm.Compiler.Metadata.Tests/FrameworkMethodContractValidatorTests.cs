using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class FrameworkMethodContractValidatorTests
{
    [Theory]
    [InlineData(MethodAttributes.Public, MethodAttributes.Public, true)]
    [InlineData(MethodAttributes.Family, MethodAttributes.Public, true)]
    [InlineData(MethodAttributes.Family, MethodAttributes.Family, true)]
    [InlineData(MethodAttributes.FamORAssem, MethodAttributes.FamORAssem, true)]
    [InlineData(MethodAttributes.Public, MethodAttributes.Family, false)]
    [InlineData(MethodAttributes.Private, MethodAttributes.Private, false)]
    [InlineData(MethodAttributes.Assembly, MethodAttributes.Public, false)]
    [InlineData(MethodAttributes.FamANDAssem, MethodAttributes.Family, false)]
    [InlineData(MethodAttributes.FamORAssem, MethodAttributes.Public, true)]
    [InlineData(MethodAttributes.FamORAssem, MethodAttributes.Family, false)]
    public void RequiresAPortableVisibilityContract(MethodAttributes source, MethodAttributes target, bool accepted)
    {
        using var original = Image.Create("Source", source | MethodAttributes.Static);
        using var replacement = Image.Create("Target", target | MethodAttributes.Static);
        Check(original, replacement, accepted);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(24, 0, true)]
    [InlineData(0, 32, true)]
    [InlineData(32, 0, false)]
    [InlineData(0, 4, false)]
    [InlineData(4, 4, true)]
    public void AcceptsRelaxedButNotStrongerGenericRequirements(int source, int target, bool accepted)
    {
        using var original = Image.Create("Source", constraints: (GenericParameterAttributes)source);
        using var replacement = Image.Create("Target", constraints: (GenericParameterAttributes)target);
        Check(original, replacement, accepted);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    public void PreservesGenericTypeConstraints(bool source, bool target, bool accepted)
    {
        using var original = Image.Create("Source", typeConstraint: source);
        using var replacement = Image.Create("Target", typeConstraint: target);
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
    [InlineData(MethodAttributes.Public | MethodAttributes.Static, MethodAttributes.Public)]
    [InlineData(MethodAttributes.Public | MethodAttributes.Virtual, MethodAttributes.Public)]
    [InlineData(MethodAttributes.Public | MethodAttributes.Virtual, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final)]
    [InlineData(MethodAttributes.Public | MethodAttributes.Virtual, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Abstract)]
    public void RejectsChangedDispatchRequirements(MethodAttributes source, MethodAttributes target)
    {
        using var original = Image.Create("Source", source);
        using var replacement = Image.Create("Target", target);
        Check(original, replacement, false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitOriginalSourceRemainsDistinctFromEqualIdentityImplementation(bool compatible)
    {
        using var original = Image.Create("Shared", MethodAttributes.Public);
        using var target = Image.Create("Shared", compatible ? MethodAttributes.Public : MethodAttributes.Private);
        var validator = Assert.IsAssignableFrom<IFrameworkMethodContractValidator>(
            new FrameworkMethodContractValidator(new Assemblies(target.Snapshot)));
        var binding = new FrameworkMethodBinding(original.Method, target.Method, "Caller.Run", 7)
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
    [InlineData(MethodAttributes.Private, false)]
    [InlineData(MethodAttributes.Private, true)]
    [InlineData(MethodAttributes.Assembly, false)]
    [InlineData(MethodAttributes.Assembly, true)]
    [InlineData(MethodAttributes.FamANDAssem, false)]
    [InlineData(MethodAttributes.FamANDAssem, true)]
    public void PreservesUnchangedNonPublicContractsOnlyWithinTheAssemblyIdentity(MethodAttributes access, bool sameAssembly)
    {
        using var original = Image.Create("Source", access);
        using var target = Image.Create(sameAssembly ? "Source" : "Target", access);
        var validator = Assert.IsAssignableFrom<IFrameworkMethodContractValidator>(
            new FrameworkMethodContractValidator(new Assemblies(target.Snapshot)));
        var binding = new FrameworkMethodBinding(original.Method, target.Method, "Caller.Run", 7)
        {
            OriginalSource = original.Snapshot,
        };
        if (sameAssembly)
            Assert.Null(Record.Exception(() => validator.Validate(binding)));
        else
            Assert.Equal(DiagnosticCode.AssemblyResolution,
                Assert.Throws<CompilerException>(() => validator.Validate(binding)).Diagnostic.Code);
    }

    // A generic parameter row the signature does not count is a different generic contract.
    [Fact]
    public void RejectsADifferentGenericParameterCount()
    {
        using var original = Image.Create("Source");
        using var replacement = Image.Create("Target", extraGenericParameter: true);
        Check(original, replacement, accepted: false);
    }

    // A constraint names its type by definition, reference or specification; a specification of a primitive array
    // is the same type in either assembly, a definition is its own assembly's.
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void ComparesConstraintsOfEveryMetadataKind(int constraintKind, bool accepted)
    {
        using var original = Image.Create("Source", typeConstraint: true, constraintKind: constraintKind);
        using var replacement = Image.Create("Target", typeConstraint: true, constraintKind: constraintKind);
        Check(original, replacement, accepted);
    }

    private static void Check(Image original, Image target, bool accepted)
    {
        var validator = Assert.IsAssignableFrom<IFrameworkMethodContractValidator>(
            new FrameworkMethodContractValidator(new Assemblies(original.Snapshot, target.Snapshot)));
        var binding = new FrameworkMethodBinding(original.Method, target.Method, "Caller.Run", 7);
        if (accepted)
        {
            Assert.Null(Record.Exception(() => validator.Validate(binding)));
            return;
        }
        var error = Assert.Throws<CompilerException>(() => validator.Validate(binding));
        Assert.Equal(DiagnosticCode.AssemblyResolution, error.Diagnostic.Code);
        Assert.Contains("Source::Run", error.Message);
        Assert.Contains("Target::Run", error.Message);
    }

    private sealed class Assemblies(params MetadataAssemblySnapshot[] values) : IMetadataAssemblyResolver
    {
        public MetadataAssemblySnapshot Resolve(AssemblyIdentity identity) => values.Single(v => v.Identity == identity);
    }

    private sealed class Image(MetadataReaderProvider provider, MetadataAssemblySnapshot snapshot,
        MethodDefinitionModel method) : IDisposable
    {
        public MetadataAssemblySnapshot Snapshot => snapshot;
        public MethodDefinitionModel Method => method;
        public void Dispose() => provider.Dispose();

        public static Image Create(string name,
            MethodAttributes attributes = MethodAttributes.Public | MethodAttributes.Static,
            GenericParameterAttributes constraints = 0, bool typeConstraint = false, bool requiredModifier = false,
            bool extraGenericParameter = false, int constraintKind = 0)
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(0, metadata.GetOrAddString(name), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
            var contract = metadata.AddAssemblyReference(metadata.GetOrAddString("Contract"), new Version(1, 0),
                default, default, 0, default);
            var constraintType = metadata.AddTypeReference(contract, metadata.GetOrAddString("Example"),
                metadata.GetOrAddString("Constraint"));
            var signature = new BlobBuilder();
            signature.WriteByte((byte)((attributes & MethodAttributes.Static) != 0 ? 0x10 : 0x30));
            signature.WriteCompressedInteger(1);
            signature.WriteCompressedInteger(0);
            if (requiredModifier)
            {
                signature.WriteByte(0x1f);
                signature.WriteCompressedInteger(CodedIndex.TypeDefOrRef(constraintType));
            }
            signature.WriteByte(0x08);
            var methodHandle = metadata.AddMethodDefinition(attributes, MethodImplAttributes.Runtime,
                metadata.GetOrAddString("Run"), metadata.GetOrAddBlob(signature), 0, MetadataTokens.ParameterHandle(1));
            var parameter = metadata.AddGenericParameter(methodHandle, constraints, metadata.GetOrAddString("T"), 0);
            if (extraGenericParameter) metadata.AddGenericParameter(methodHandle, 0, metadata.GetOrAddString("U"), 1);
            if (typeConstraint)
            {
                // 0: a reference; 1: this assembly's own definition; 2: a specification of int[].
                EntityHandle constraint = constraintKind switch
                {
                    1 => metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString("Example"),
                        metadata.GetOrAddString("Local"), default, MetadataTokens.FieldDefinitionHandle(1),
                        MetadataTokens.MethodDefinitionHandle(1)),
                    2 => metadata.AddTypeSpecification(metadata.GetOrAddBlob(new byte[] { 0x1d, 0x08 })),
                    _ => constraintType,
                };
                metadata.AddGenericParameterConstraint(parameter, constraint);
            }
            var bytes = new BlobBuilder();
            new MetadataRootBuilder(metadata).Serialize(bytes, 0, 0);
            var reader = MetadataReaderProvider.FromMetadataImage(bytes.ToImmutableArray());
            var identity = new AssemblyIdentity(name);
            var method = new MethodDefinitionModel(new(identity, MetadataTokens.GetToken(methodHandle)),
                new(identity, 0x02000001), "Run", (attributes & MethodAttributes.Static) != 0,
                MethodSignatureModel.Create(CliValueKind.I4), 0)
            { GenericArity = 1 };
            var snapshot = new MetadataAssemblySnapshot(identity, reader.GetMetadataReader(),
                ImmutableDictionary<int, TypeDefinitionModel>.Empty,
                ImmutableDictionary<int, FieldDefinitionModel>.Empty,
                ImmutableDictionary<int, MethodDefinitionModel>.Empty.Add(method.Key.MetadataToken, method),
                ImmutableDictionary<int, EntityHandle>.Empty);
            return new Image(reader, snapshot, method);
        }
    }
}
