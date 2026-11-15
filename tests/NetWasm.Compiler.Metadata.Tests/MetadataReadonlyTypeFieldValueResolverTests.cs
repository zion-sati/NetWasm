// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataReadonlyTypeFieldValueResolverTests
{
    private static readonly AssemblyIdentity Assembly = new("ReadonlyProofs");
    private static readonly EntityKey SystemTypeKey = new(new("Core"), 0x02000001);
    private static readonly CliTypeIdentity SystemType = CliTypeIdentity.Named(SystemTypeKey.Assembly, "System", "Type", false);
    private static readonly CliTypeIdentity Integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
    private static readonly CliTypeIdentity Text = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryDefinitionKeysUseTheSameInitializerAndAccessorExposureProof(bool exposed)
    {
        using var fixture = new Fixture("nongeneric-alias");
        if (exposed) fixture.ExposureAssembly = Assembly;
        var resolver = Assert.IsAssignableFrom<IReadonlyTypeFieldValueResolver>(fixture.Create());
        var field = fixture.Field(Integer);

        Assert.Equal(exposed ? null : Integer, resolver.Resolve(field.Definition.Key));
        Assert.Equal(exposed ? null : Integer, resolver.Resolve(field));
        Assert.Equal(exposed ? 0 : 2, fixture.BodyReads);
        Assert.Equal(exposed ? 0 : 2, fixture.MaterializerReads);
    }

    [Fact]
    public void CachedInitializerTokensAreResolvedSeparatelyForEachClosedType()
    {
        using var fixture = new Fixture();
        var resolver = fixture.Create();
        Assert.Same(Integer, resolver.Resolve(fixture.Field(Integer)));
        Assert.Same(Text, resolver.Resolve(fixture.Field(Text)));
        Assert.Same(Integer, resolver.Resolve(fixture.Field(Integer)));
        Assert.Equal(2, fixture.BodyReads);
        Assert.Equal(2, fixture.MaterializerReads);
        Assert.Equal([Integer, Integer, Text, Text, Integer, Integer], fixture.TypeArguments);
    }

    [Theory]
    [InlineData("static")]
    [InlineData("mutable")]
    [InlineData("value-owner")]
    [InlineData("other-field-type")]
    [InlineData("other-type-assembly")]
    [InlineData("other-type-name")]
    [InlineData("public")]
    public void IneligibleFieldsDoNotReadBodies(string shape)
    {
        using var fixture = new Fixture(shape);
        var field = fixture.Field(Integer);
        field = shape switch
        {
            "static" => field with { Definition = field.Definition with { IsStatic = true } },
            "mutable" => field with { Definition = field.Definition with { IsInitOnly = false } },
            "value-owner" => field with { DeclaringType = CliTypeIdentity.Named(Assembly, "Test", "Value", true) },
            "other-field-type" => field with { FieldType = Integer },
            "other-type-assembly" => field with { FieldType = CliTypeIdentity.Named(Assembly, "System", "Type", false) },
            "other-type-name" => field with { FieldType = CliTypeIdentity.Named(SystemTypeKey.Assembly, "System", "Attribute", false) },
            _ => field,
        };
        Assert.Null(fixture.Create().Resolve(field));
        Assert.Equal(0, fixture.BodyReads);
        Assert.Equal(0, fixture.MaterializerReads);
        Assert.Empty(fixture.TypeArguments);
    }

    [Theory]
    [InlineData("short-prefix")]
    [InlineData("wrong-receiver")]
    [InlineData("wrong-token-opcode")]
    [InlineData("wrong-call-opcode")]
    [InlineData("wrong-store-opcode")]
    [InlineData("wrong-field")]
    [InlineData("bodyless-constructor")]
    [InlineData("bodyless-other")]
    [InlineData("bodyless-static")]
    [InlineData("unreadable")]
    [InlineData("no-constructor")]
    public void MissingOrUnprovableInitializersAreRejectedAndCached(string shape)
    {
        using var fixture = new Fixture(shape);
        var resolver = fixture.Create();
        Assert.Null(resolver.Resolve(fixture.Field(Integer)));
        var reads = fixture.BodyReads;
        Assert.Null(resolver.Resolve(fixture.Field(Text)));
        Assert.Equal(reads, fixture.BodyReads);
        Assert.Empty(fixture.TypeArguments);
    }

    [Theory]
    [InlineData("materializer-owner")]
    [InlineData("materializer-name")]
    [InlineData("materializer-instance")]
    [InlineData("materializer-arity")]
    public void InitializersRequireTheBoundTypeMaterializer(string shape)
    {
        using var fixture = new Fixture(shape);
        Assert.Null(fixture.Create().Resolve(fixture.Field(Integer)));
        Assert.Equal(1, fixture.MaterializerReads);
        Assert.Empty(fixture.TypeArguments);
    }

    [Theory]
    [InlineData("address")]
    [InlineData("static-address")]
    [InlineData("static-store")]
    [InlineData("second-store")]
    public void WritesAndAddressEscapesOutsideTheInitializerInvalidateTheProof(string shape)
    {
        using var fixture = new Fixture(shape);
        Assert.Null(fixture.Create().Resolve(fixture.Field(Integer)));
        Assert.Empty(fixture.TypeArguments);
    }

    [Theory]
    [InlineData("definition-alias")]
    [InlineData("reference-alias")]
    [InlineData("constructed-alias")]
    [InlineData("nongeneric-alias")]
    public void MemberReferencesToTheSameFieldAreRecognized(string shape)
    {
        using var fixture = new Fixture(shape);
        Assert.Same(Integer, fixture.Create().Resolve(fixture.Field(Integer)));
        Assert.Equal(2, fixture.MaterializerReads);
    }

    [Theory]
    [InlineData("other-name")]
    [InlineData("other-owner")]
    [InlineData("other-assembly")]
    [InlineData("module-parent")]
    [InlineData("nonfield-token")]
    public void UnrelatedFieldReferencesDoNotInvalidateTheProof(string shape)
    {
        using var fixture = new Fixture(shape);
        Assert.Same(Integer, fixture.Create().Resolve(fixture.Field(Integer)));
        Assert.Equal(2, fixture.MaterializerReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitializerTokensMustNameTypesAndAgreeAcrossConstructors(bool invalidToken)
    {
        using var fixture = new Fixture(invalidToken ? "invalid-type-token" : "different-types");
        Assert.Null(fixture.Create().Resolve(fixture.Field(Integer)));
        Assert.Equal(invalidToken ? 0 : 2, fixture.TypeArguments.Count);
    }

    [Fact]
    public void ASameNameFieldWithAnotherSignatureCannotSupplyTheInitializer()
    {
        using var fixture = new Fixture("signature-mismatch");
        Assert.Null(fixture.Create().Resolve(fixture.Field(Integer)));
        Assert.Empty(fixture.TypeArguments);
    }

    [Fact]
    public void AMethodMemberReferenceCannotSupplyAFieldInitializer()
    {
        using var fixture = new Fixture("method-reference");
        Assert.Null(fixture.Create().Resolve(fixture.Field(Integer)));
        Assert.Empty(fixture.TypeArguments);
    }

    [Fact]
    public void ABranchCannotReenterTheExemptInitializerStore()
    {
        using var fixture = new Fixture("reenter-store");
        Assert.Null(fixture.Create().Resolve(fixture.Field(Integer)));
        Assert.Empty(fixture.TypeArguments);
    }

    [Fact]
    public void ASwitchCannotReenterTheInitializerPrefix()
    {
        using var fixture = new Fixture("switch-prefix");
        Assert.Null(fixture.Create().Resolve(fixture.Field(Integer)));
        Assert.Empty(fixture.TypeArguments);
    }

    [Theory]
    [InlineData("protected-prefix")]
    [InlineData("handler-prefix")]
    [InlineData("filter-prefix")]
    public void ExceptionalControlFlowCannotBypassInitialization(string shape)
    {
        using var fixture = new Fixture(shape);
        Assert.Null(fixture.Create().Resolve(fixture.Field(Integer)));
        Assert.Empty(fixture.TypeArguments);
    }

    [Theory]
    [InlineData("branch-after")]
    [InlineData("switch-after")]
    [InlineData("catch-after")]
    [InlineData("filter-after")]
    public void ConstructorControlFlowAfterInitializationRemainsEligible(string shape)
    {
        using var fixture = new Fixture(shape);
        Assert.Same(Integer, fixture.Create().Resolve(fixture.Field(Integer)));
        Assert.Equal(2, fixture.MaterializerReads);
    }

    [Fact]
    public void MissingFieldFailsBeforeReadingMetadata()
    {
        using var fixture = new Fixture();
        Assert.Throws<ArgumentNullException>(() => fixture.Create().Resolve(null!));
        Assert.Equal(0, fixture.BodyReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AccessorExposureInAnyMaterializedAssemblyRejectsProofBeforeReadingBodies(bool external)
    {
        using var fixture = new Fixture();
        using var other = new Fixture("external");
        fixture.ExposureAssembly = external ? new("External") : Assembly;
        var resolver = fixture.Create(other);
        Assert.Null(resolver.Resolve(fixture.Field(Integer)));
        var checks = fixture.ExposureChecks;
        Assert.Null(resolver.Resolve(fixture.Field(Text)));
        Assert.Equal(checks, fixture.ExposureChecks);
        Assert.Equal(0, fixture.BodyReads);
        Assert.Equal(0, fixture.MaterializerReads);
    }

    private sealed class Fixture : IDisposable, IMethodInstanceResolver,
        IMetadataTypeSignatureResolver, IRawCilInstructionReader, IUnsafeAccessorFieldExposureClassifier
    {
        private readonly string _shape;
        private readonly ManagedAssembly _assembly;
        private readonly int _reference;
        private readonly FieldDefinitionModel _field;
        private readonly CliTypeIdentity _owner;

        internal Fixture(string shape = "valid")
        {
            _shape = shape;
            // Only the adapter's PE/metadata input is real. Its behavioral
            // collaborators are substituted; no compiler or filesystem runs.
            var metadata = new MetadataBuilder();
            metadata.AddModule(0, metadata.GetOrAddString("ReadonlyProofs.dll"), default, default, default);
            metadata.AddAssembly(metadata.GetOrAddString(shape == "external" ? "External" : Assembly.Name), new(1, 0), default, default, 0, 0);
            var core = metadata.AddAssemblyReference(metadata.GetOrAddString("Core"), new(1, 0), default, default, 0, default);
            var systemType = metadata.AddTypeReference(core, metadata.GetOrAddString("System"), metadata.GetOrAddString("Type"));
            var exceptionType = metadata.AddTypeReference(core, metadata.GetOrAddString("System"), metadata.GetOrAddString("Exception"));
            metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"),
                default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            var owner = metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString("Test"),
                metadata.GetOrAddString(shape == "nongeneric-alias" ? "Holder" : "Holder`1"), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            if (shape != "nongeneric-alias")
                metadata.AddGenericParameter(owner, GenericParameterAttributes.None, metadata.GetOrAddString("T"), 0);
            var fieldSignature = new BlobBuilder();
            new BlobEncoder(fieldSignature).FieldSignature().Type(systemType, false);
            var field = metadata.AddFieldDefinition(FieldAttributes.InitOnly |
                (shape == "public" ? FieldAttributes.Public : FieldAttributes.Private),
                metadata.GetOrAddString("_type"), metadata.GetOrAddBlob(fieldSignature));
            var referenceSignature = fieldSignature;
            if (shape is "signature-mismatch" or "method-reference")
            {
                referenceSignature = new BlobBuilder();
                if (shape == "signature-mismatch")
                {
                    new BlobEncoder(referenceSignature).FieldSignature().Object();
                    metadata.AddFieldDefinition(FieldAttributes.Private | FieldAttributes.InitOnly,
                        metadata.GetOrAddString("_type"), metadata.GetOrAddBlob(referenceSignature));
                }
                else new BlobEncoder(referenceSignature).MethodSignature()
                    .Parameters(0, result => result.Void(), _ => { });
            }
            var current = metadata.AddAssemblyReference(metadata.GetOrAddString(shape == "other-assembly" ? "Other" : Assembly.Name),
                new(1, 0), default, default, 0, default);
            var typeReference = metadata.AddTypeReference(current, metadata.GetOrAddString("Test"),
                metadata.GetOrAddString(shape == "other-owner" ? "Other`1" :
                    shape == "nongeneric-alias" ? "Holder" : "Holder`1"));
            var constructed = new BlobBuilder();
            new BlobEncoder(constructed).TypeSpecificationSignature().GenericInstantiation(owner, 1, false).AddArgument().Int32();
            EntityHandle parent = shape switch
            {
                "definition-alias" => owner,
                "constructed-alias" => metadata.AddTypeSpecification(metadata.GetOrAddBlob(constructed)),
                "module-parent" => metadata.AddModuleReference(metadata.GetOrAddString("Unrelated")),
                _ => typeReference,
            };
            _reference = MetadataTokens.GetToken(metadata.AddMemberReference(parent,
                metadata.GetOrAddString(shape == "other-name" ? "Other" : "_type"), metadata.GetOrAddBlob(referenceSignature)));
            var il = new BlobBuilder();
            var encoder = new MethodBodyStreamEncoder(il);
            for (var index = 0; index < 2; index++)
            {
                var code = new BlobBuilder();
                code.WriteByte((byte)index);
                int offset;
                if (shape is "protected-prefix" or "handler-prefix" or "filter-prefix" or "catch-after" or "filter-after")
                {
                    var body = encoder.AddMethodBody(codeSize: 12, maxStack: 1, exceptionRegionCount: 1,
                        hasSmallExceptionRegions: true, localVariablesSignature: default, attributes: MethodBodyAttributes.None);
                    var writer = new BlobWriter(body.Instructions);
                    writer.WriteByte((byte)index);
                    writer.WriteBytes(0, 11);
                    var filter = shape is "filter-prefix" or "filter-after";
                    body.ExceptionRegions.Add(filter ? ExceptionRegionKind.Filter : ExceptionRegionKind.Catch,
                        shape == "protected-prefix" ? 0 : 5, 2,
                        shape == "handler-prefix" ? 0 : 9, 3,
                        filter ? default : exceptionType, shape == "filter-prefix" ? 0 : 7);
                    offset = body.Offset;
                }
                else offset = encoder.AddMethodBody(new InstructionEncoder(code), maxStack: 1);
                var signature = new BlobBuilder();
                new BlobEncoder(signature).MethodSignature(isInstanceMethod: shape != "bodyless-static")
                    .Parameters(index, returns => returns.Void(), parameters =>
                    {
                        if (index != 0) parameters.AddParameter().Type().Int32();
                    });
                metadata.AddMethodDefinition(MethodAttributes.Public |
                    (shape == "bodyless-static" ? MethodAttributes.Static : 0), MethodImplAttributes.IL,
                    metadata.GetOrAddString(shape is "no-constructor" or "bodyless-other" ? "Other" : ".ctor"),
                    metadata.GetOrAddBlob(signature), shape.StartsWith("bodyless-", StringComparison.Ordinal) ? -1 : offset,
                    MetadataTokens.ParameterHandle(1));
            }
            metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString("Test"),
                metadata.GetOrAddString("Unrelated"), default,
                MetadataTokens.FieldDefinitionHandle(shape == "signature-mismatch" ? 3 : 2),
                MetadataTokens.MethodDefinitionHandle(3));
            var unrelatedSignature = new BlobBuilder();
            new BlobEncoder(unrelatedSignature).MethodSignature(isInstanceMethod: true)
                .Parameters(0, returns => returns.Void(), _ => { });
            metadata.AddMethodDefinition(MethodAttributes.Public, MethodImplAttributes.IL,
                metadata.GetOrAddString(".ctor"), metadata.GetOrAddBlob(unrelatedSignature), -1,
                MetadataTokens.ParameterHandle(1));
            var image = new BlobBuilder();
            new ManagedPEBuilder(new(imageCharacteristics: Characteristics.Dll), new(metadata), il).Serialize(image);
            _assembly = ManagedAssembly.Parse("memory.dll", image.ToArray(), AssemblyIdentityAliases.Empty,
                new ValueTypeDefinitionStackKindResolver(), new NativeImportDeclarationReader());
            _field = _assembly.Fields[MetadataTokens.GetToken(field)];
            _owner = CliTypeIdentity.FromDefinition(_assembly.Types[MetadataTokens.GetToken(owner)]);
        }

        internal int BodyReads { get; private set; }
        internal int MaterializerReads { get; private set; }
        internal List<CliTypeIdentity> TypeArguments { get; } = [];
        internal AssemblyIdentity? ExposureAssembly { get; set; }
        internal int ExposureChecks { get; private set; }

        internal MetadataReadonlyTypeFieldValueResolver Create(Fixture? other = null) => new(
            other is null
                ? ImmutableDictionary<AssemblyIdentity, ManagedAssembly>.Empty.Add(Assembly, _assembly)
                : ImmutableDictionary<AssemblyIdentity, ManagedAssembly>.Empty.Add(Assembly, _assembly).Add(other._assembly.Identity, other._assembly),
            this, this, this, this, SystemTypeKey);

        public bool Classify(MethodDefinitionModel method, FieldInstanceModel field)
        {
            Assert.Equal(_field, field.Definition);
            ExposureChecks++;
            return method.Key.Assembly == ExposureAssembly;
        }

        internal FieldInstanceModel Field(CliTypeIdentity argument) => new(_field,
            _shape == "nongeneric-alias" ? _owner : CliTypeIdentity.GenericInstantiation(_owner, [argument]), SystemType);

        public ImmutableArray<RawCilInstruction> Read(ReadOnlySpan<byte> bytes)
        {
            BodyReads++;
            if (_shape == "unreadable") throw new BadImageFormatException();
            if (_shape == "no-constructor") return [new(0, OpCodes.Ret, null)];
            var body = ImmutableArray.CreateBuilder<RawCilInstruction>();
            body.Add(new(0, OpCodes.Nop, null));
            body.Add(new(1, _shape == "wrong-receiver" ? OpCodes.Ldnull : OpCodes.Ldarg_0, null));
            body.Add(new(2, _shape == "wrong-token-opcode" ? OpCodes.Ldnull : OpCodes.Ldtoken,
                _shape == "invalid-type-token" ? 0x04000001 : 0x02000001 + bytes[0]));
            body.Add(new(3, _shape == "wrong-call-opcode" ? OpCodes.Callvirt : OpCodes.Call, 0x0a000002));
            body.Add(new(4, _shape == "wrong-store-opcode" ? OpCodes.Ldfld : OpCodes.Stfld,
                _shape is "definition-alias" or "reference-alias" or "constructed-alias" or "nongeneric-alias" or
                    "signature-mismatch" or "method-reference" ? _reference :
                _shape == "wrong-field" ? 0x04000002 : _field.Key.MetadataToken));
            if (_shape == "short-prefix") return body.Take(3).ToImmutableArray();
            if (_shape is "address" or "static-address" or "static-store" or "second-store")
                body.Add(new(5, _shape switch
                {
                    "address" => OpCodes.Ldflda,
                    "static-address" => OpCodes.Ldsflda,
                    "static-store" => OpCodes.Stsfld,
                    _ => OpCodes.Stfld,
                }, _field.Key.MetadataToken));
            if (_shape is "other-name" or "other-owner" or "other-assembly" or "module-parent" or "nonfield-token")
                body.Add(new(5, OpCodes.Stfld, _shape == "nonfield-token" ? 0x06000001 : _reference));
            if (_shape is "reenter-store" or "branch-after" or "switch-prefix" or "switch-after")
                body.Add(new(5, _shape.StartsWith("switch", StringComparison.Ordinal) ? OpCodes.Switch : OpCodes.Br, null)
                {
                    BranchTargets = _shape switch
                    {
                        "reenter-store" => [4],
                        "switch-prefix" => [6, 1],
                        _ => [6],
                    },
                });
            body.Add(new(6, OpCodes.Ldfld, _field.Key.MetadataToken));
            body.Add(new(7, OpCodes.Ret, null));
            return body.ToImmutable();
        }

        public MethodInstanceModel ResolveMethodInstance(AssemblyIdentity source, int metadataToken,
            string methodDisplayName, int ilOffset, CliGenericContext? genericContext = null)
        {
            Assert.Equal(Assembly, source);
            Assert.Equal(0x0a000002, metadataToken);
            Assert.Equal(3, ilOffset);
            Assert.Equal(".ctor", methodDisplayName);
            MaterializerReads++;
            var signature = new MethodSignatureModel(SystemType, _shape == "materializer-arity" ? [] : [Integer]);
            return new(new(new(SystemTypeKey.Assembly, 0x06000001),
                _shape == "materializer-owner" ? new(Assembly, 0x02000001) : SystemTypeKey,
                _shape == "materializer-name" ? "Other" : "GetTypeFromHandle",
                _shape != "materializer-instance", signature, 1), SystemType, [], signature);
        }

        public CliTypeIdentity Resolve(MetadataAssemblySnapshot source, int metadataToken,
            CliGenericContext? genericContext = null)
        {
            Assert.Same(_assembly.Metadata, source);
            var argument = _shape == "nongeneric-alias" ? Integer : Assert.Single(genericContext!.Value.TypeArguments);
            if (_shape == "nongeneric-alias") Assert.Empty(genericContext!.Value.TypeArguments);
            Assert.Empty(genericContext!.Value.MethodArguments);
            TypeArguments.Add(argument);
            return _shape == "different-types" && metadataToken == 0x02000002 ? Text : argument;
        }

        public void Dispose() => _assembly.Dispose();
    }
}
