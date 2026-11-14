using System.Collections.Immutable;
using System.Reflection.Metadata;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.UnsafeAccessors;
using NetWasm.Compiler.Metadata.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata.Tests.UnsafeAccessors;

public sealed class UnsafeAccessorTargetResolverTests
{
    private static readonly AssemblyIdentity Assembly = new("Accessors");
    private static readonly CliTypeIdentity Owner = CliTypeIdentity.Named(Assembly, "Test", "Owner", false);
    private static readonly CliTypeIdentity Integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
    private static readonly CliTypeIdentity Void = CliTypeIdentity.FromStackKind(CliValueKind.Void);
    private static readonly CliTypeIdentity ObjectType = CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference, isValueType: false);
    private static readonly CliTypeIdentity TypeVariable = CliTypeIdentity.GenericParameter(false, 0);
    private static readonly CliTypeIdentity MethodVariable = CliTypeIdentity.GenericParameter(true, 0);

    [Theory]
    [InlineData("malformed")]
    [InlineData("instance-declaration")]
    [InlineData("unknown-kind")]
    [InlineData("constructor-void")]
    [InlineData("constructor-byref")]
    [InlineData("constructor-name")]
    [InlineData("missing-owner")]
    [InlineData("field-arity")]
    [InlineData("field-value-return")]
    [InlineData("type-variable-owner")]
    [InlineData("method-variable-owner")]
    [InlineData("nested-byref-owner")]
    [InlineData("pointer-owner")]
    [InlineData("void-owner")]
    [InlineData("value-method-receiver")]
    [InlineData("value-field-receiver")]
    public void InvalidDeclarationsBindBadImageFormatWithoutSelectingAMember(string shape)
    {
        var fixture = new Fixture();
        var accessor = fixture.Accessor();
        switch (shape)
        {
            case "malformed": accessor = Change(accessor, accessor.Definition.UnsafeAccessor! with { IsMalformed = true }); break;
            case "instance-declaration": accessor = accessor with { Definition = accessor.Definition with { IsStatic = false } }; break;
            case "unknown-kind": accessor = Change(accessor, accessor.Definition.UnsafeAccessor! with { Kind = 99 }); break;
            case "constructor-void": accessor = fixture.Accessor(UnsafeAccessorMemberKind.Constructor); fixture.Signature = Signature(Void); break;
            case "constructor-byref": accessor = fixture.Accessor(UnsafeAccessorMemberKind.Constructor); fixture.Signature = Signature(CliTypeIdentity.ManagedByReference(Owner)); break;
            case "constructor-name": accessor = Change(fixture.Accessor(UnsafeAccessorMemberKind.Constructor), new(0, ".ctor", true, false)); fixture.Signature = Signature(Owner); break;
            case "missing-owner": fixture.Signature = Signature(Void); break;
            case "field-arity": accessor = fixture.Accessor(UnsafeAccessorMemberKind.Field); fixture.Signature = Signature(CliTypeIdentity.ManagedByReference(Integer), Owner, Integer); break;
            case "field-value-return": accessor = fixture.Accessor(UnsafeAccessorMemberKind.Field); fixture.Signature = Signature(Integer, Owner); break;
            case "type-variable-owner": fixture.Signature = Signature(Void, TypeVariable); break;
            case "method-variable-owner": fixture.Signature = Signature(Void, MethodVariable); break;
            case "nested-byref-owner": fixture.Signature = Signature(Void, CliTypeIdentity.ManagedByReference(CliTypeIdentity.ManagedByReference(Owner))); break;
            case "pointer-owner": fixture.Signature = Signature(Void, CliTypeIdentity.UnmanagedPointer(Owner)); break;
            case "void-owner": fixture.Signature = Signature(Void, Void); break;
            case "value-method-receiver": fixture.OwnerDefinition = fixture.OwnerDefinition with { IsValueType = true }; break;
            case "value-field-receiver": fixture.OwnerDefinition = fixture.OwnerDefinition with { IsValueType = true }; accessor = fixture.Accessor(UnsafeAccessorMemberKind.Field); fixture.Signature = Signature(CliTypeIdentity.ManagedByReference(Integer), Owner); break;
        }
        var failure = Assert.IsType<UnsafeAccessorBinding.Failure>(fixture.Create().Resolve(accessor));
        Assert.Equal("System.BadImageFormatException", failure.ExceptionConstructor.DeclaringType.FullName);
        Assert.Equal(".ctor", failure.ExceptionConstructor.Definition.Name);
        Assert.Empty(failure.ExceptionConstructor.MethodArguments);
        Assert.Empty(fixture.Requests);
        Assert.Equal(0, fixture.StackResolutions);
    }

    [Theory]
    [InlineData("vector-owner")]
    [InlineData("array-owner")]
    public void UnsupportedShapesProduceAnExplicitMetadataDiagnostic(string shape)
    {
        var fixture = new Fixture();
        var accessor = fixture.Accessor();
        fixture.Signature = Signature(Void, shape == "vector-owner" ? CliTypeIdentity.SzArray(Owner) : CliTypeIdentity.Array(Owner, 2));
        var exception = Assert.Throws<CompilerException>(() => fixture.Create().Resolve(accessor));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Empty(fixture.Requests);
        Assert.Empty(fixture.ExceptionNames);
    }

    [Theory]
    [InlineData(false, null, "Access")]
    [InlineData(true, null, "")]
    [InlineData(true, "", "")]
    [InlineData(true, "Target", "Target")]
    public void NamesDistinguishOmissionFromExplicitNullOrEmpty(bool specified, string? name, string expected)
    {
        var fixture = new Fixture();
        var accessor = Change(fixture.Accessor(), new(1, name, specified, false));
        fixture.Create().Resolve(accessor);
        Assert.Equal(expected, Assert.Single(fixture.Requests).Name);
    }

    [Theory]
    [InlineData((int)UnsafeAccessorFailure.MissingMethod, "System.MissingMethodException")]
    [InlineData((int)UnsafeAccessorFailure.MissingField, "System.MissingFieldException")]
    [InlineData((int)UnsafeAccessorFailure.AmbiguousMatch, "System.Reflection.AmbiguousMatchException")]
    [InlineData((int)UnsafeAccessorFailure.InvalidProgram, "System.InvalidProgramException")]
    public void MemberFailuresBindOnlyTheirRequestedExceptionConstructor(int kind, string name)
    {
        var fixture = new Fixture { Match = new UnsafeAccessorMemberMatch.Failure((UnsafeAccessorFailure)kind) };
        var failure = Assert.IsType<UnsafeAccessorBinding.Failure>(fixture.Create().Resolve(fixture.Accessor()));
        Assert.Equal(name, failure.ExceptionConstructor.DeclaringType.FullName);
        Assert.Equal(name, Assert.Single(fixture.ExceptionNames));
        Assert.Equal(0, fixture.StackResolutions);
    }

    [Theory]
    [InlineData(UnsafeAccessorMemberKind.Method)]
    [InlineData(UnsafeAccessorMemberKind.StaticMethod)]
    [InlineData(UnsafeAccessorMemberKind.Constructor)]
    public void ClosedMethodsSubstituteOwnerAndMethodArgumentsAfterTypicalMemberSelection(UnsafeAccessorMemberKind kind)
    {
        var fixture = new Fixture();
        var genericOwner = CliTypeIdentity.GenericInstantiation(Owner, [TypeVariable]);
        fixture.Signature = kind == UnsafeAccessorMemberKind.Constructor ? Signature(genericOwner, TypeVariable) : Signature(TypeVariable, genericOwner, MethodVariable);
        var target = fixture.Target with
        {
            IsStatic = kind == UnsafeAccessorMemberKind.StaticMethod,
            GenericArity = kind == UnsafeAccessorMemberKind.Constructor ? 0 : 1,
            Signature = new(kind == UnsafeAccessorMemberKind.Constructor ? Void : TypeVariable,
                [kind == UnsafeAccessorMemberKind.Constructor ? TypeVariable : MethodVariable])
        };
        fixture.Match = new UnsafeAccessorMemberMatch.Method(target);
        var accessor = fixture.Accessor(kind) with
        {
            DeclaringType = CliTypeIdentity.GenericInstantiation(Owner, [Integer]),
            MethodArguments = kind == UnsafeAccessorMemberKind.Constructor ? [] : [Owner]
        };
        var binding = fixture.Create().Resolve(accessor);
        var instance = kind == UnsafeAccessorMemberKind.Constructor
            ? Assert.IsType<UnsafeAccessorBinding.Constructor>(binding).Target
            : Assert.IsType<UnsafeAccessorBinding.Method>(binding).Target;
        Assert.Equal(Integer, Assert.Single(instance.DeclaringType.TypeArguments));
        if (kind == UnsafeAccessorMemberKind.Constructor) Assert.Empty(instance.MethodArguments);
        else Assert.Equal(Owner, Assert.Single(instance.MethodArguments));
        Assert.Equal(kind == UnsafeAccessorMemberKind.Constructor ? Integer : Owner, Assert.Single(instance.Signature.ParameterSignatureTypes));
        Assert.Equal(kind == UnsafeAccessorMemberKind.Constructor ? Void : Integer, instance.Signature.ReturnSignatureType);
        var request = Assert.Single(fixture.Requests);
        Assert.Same(fixture.Signature.ReturnType, request.Signature.ReturnType);
        Assert.Equal(kind, request.Kind);
        Assert.Equal(kind == UnsafeAccessorMemberKind.Constructor ? ".ctor" : "Access", request.Name);
        Assert.Empty(fixture.ExceptionNames);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FieldsUseClosedOwnerArgumentsAndTheirOwnDeclarationSignature(bool isStatic)
    {
        var fixture = new Fixture();
        fixture.Signature = Signature(CliTypeIdentity.ManagedByReference(TypeVariable), CliTypeIdentity.GenericInstantiation(Owner, [TypeVariable]));
        var field = new FieldDefinitionModel(new(Assembly, 0x04000001), fixture.OwnerDefinition.Key, "Field", TypeVariable, isStatic);
        fixture.Match = new UnsafeAccessorMemberMatch.Field(field);
        var accessor = fixture.Accessor(isStatic ? UnsafeAccessorMemberKind.StaticField : UnsafeAccessorMemberKind.Field) with
        { DeclaringType = CliTypeIdentity.GenericInstantiation(Owner, [Integer]) };
        var binding = Assert.IsType<UnsafeAccessorBinding.Field>(fixture.Create().Resolve(accessor));
        Assert.Equal(Integer, binding.Target.FieldType);
        Assert.Equal(Integer, Assert.Single(binding.Target.DeclaringType.TypeArguments));
        Assert.Same(field, binding.Target.Definition);
    }

    [Fact]
    public void ModifiedByrefValueReceiverKeepsTheUnmodifiedOwnerForBinding()
    {
        var fixture = new Fixture { OwnerDefinition = new(new(Assembly, 0x02000001), "Test", "Owner", true, [], []) };
        fixture.Signature = Signature(Void, CliTypeIdentity.Modified(CliTypeIdentity.ManagedByReference(CliTypeIdentity.Modified(Owner, Owner, true)), Owner, false));
        Assert.IsType<UnsafeAccessorBinding.Method>(fixture.Create().Resolve(fixture.Accessor()));
        Assert.Equal(Owner, fixture.ResolvedOwner);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("replaced")]
    public void RegistryRequiresExactlyEveryDeclaredKind(string shape)
    {
        var fixture = new Fixture();
        var registry = fixture.Registry;
        if (shape != "extra") registry = registry.Remove(UnsafeAccessorMemberKind.Field);
        if (shape != "missing") registry = registry.Add((UnsafeAccessorMemberKind)99, fixture);
        Assert.Throws<ArgumentException>(() => fixture.Create(registry));
    }

    [Fact]
    public void NullAndUnannotatedMethodsFailBeforeMetadataWork()
    {
        var fixture = new Fixture();
        var resolver = fixture.Create();
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(null!));
        var accessor = fixture.Accessor();
        Assert.Throws<ArgumentException>(() => resolver.Resolve(accessor with { Definition = accessor.Definition with { UnsafeAccessor = null } }));
        Assert.Empty(fixture.Requests);
    }

    // An UnsafeAccessorTypeAttribute position is declared object and stands for the type it names, so the
    // member is matched against that type: here the owner a static method accessor passes first.
    [Fact]
    public void TranslatedParametersAreMatchedAsTheTypeTheyName()
    {
        var fixture = new Fixture { Signature = Signature(Void, ObjectType, ObjectType) };
        fixture.NamedTypes["Test.Owner, Accessors"] = fixture.OwnerDefinition;
        var accessor = Change(fixture.Accessor(UnsafeAccessorMemberKind.StaticMethod),
            new((int)UnsafeAccessorMemberKind.StaticMethod, null, false, false, new([null, "Test.Owner, Accessors"])));

        Assert.IsType<UnsafeAccessorBinding.Method>(fixture.Create().Resolve(accessor));

        var request = Assert.Single(fixture.Requests);
        Assert.Equal(CliTypeIdentity.FromDefinition(fixture.OwnerDefinition), request.Signature.ParameterTypes[0]);
        Assert.Equal(ObjectType, request.Signature.ParameterTypes[1]);
        Assert.Equal(new[] { ("Test.Owner, Accessors", Assembly) }, fixture.TypeNameRequests);
    }

    [Fact]
    public void TranslatedReturnValuesAreMatchedAsTheTypeTheyName()
    {
        var fixture = new Fixture { Signature = Signature(ObjectType) };
        fixture.NamedTypes["Test.Owner, Accessors"] = fixture.OwnerDefinition;
        var accessor = Change(fixture.Accessor(UnsafeAccessorMemberKind.Constructor),
            new((int)UnsafeAccessorMemberKind.Constructor, null, false, false, new(["Test.Owner, Accessors"])));

        Assert.IsType<UnsafeAccessorBinding.Constructor>(fixture.Create().Resolve(accessor));

        Assert.Equal(CliTypeIdentity.FromDefinition(fixture.OwnerDefinition), Assert.Single(fixture.Requests).Signature.ReturnType);
    }

    // The CLR fails to load a type the attribute names but the program does not define when the accessor runs.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UndefinedTranslatedTypesBindTypeLoadFailures(bool returnValue)
    {
        var fixture = new Fixture { Signature = returnValue ? Signature(ObjectType) : Signature(Void, ObjectType) };
        var kind = returnValue ? UnsafeAccessorMemberKind.Constructor : UnsafeAccessorMemberKind.StaticMethod;
        var accessor = Change(fixture.Accessor(kind),
            new((int)kind, null, false, false, new(returnValue ? ["Missing.Type"] : [null, "Missing.Type"])));

        var failure = Assert.IsType<UnsafeAccessorBinding.Failure>(fixture.Create().Resolve(accessor));

        Assert.Equal("System.TypeLoadException", failure.ExceptionConstructor.DeclaringType.FullName);
        Assert.Empty(fixture.Requests);
    }

    [Theory]
    [InlineData("not-object")]
    [InlineData("value-type")]
    [InlineData("generic")]
    public void UnsupportedTranslationsProduceAnExplicitMetadataDiagnostic(string shape)
    {
        var fixture = new Fixture { Signature = Signature(Void, shape == "not-object" ? Integer : ObjectType) };
        fixture.NamedTypes["Test.Owner"] = shape switch
        {
            "value-type" => fixture.OwnerDefinition with { IsValueType = true },
            "generic" => fixture.OwnerDefinition with { GenericArity = 1 },
            _ => fixture.OwnerDefinition,
        };
        var accessor = Change(fixture.Accessor(UnsafeAccessorMemberKind.StaticMethod),
            new((int)UnsafeAccessorMemberKind.StaticMethod, null, false, false, new([null, "Test.Owner"])));

        var exception = Assert.Throws<CompilerException>(() => fixture.Create().Resolve(accessor));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Empty(fixture.Requests);
    }

    [Theory]
    [InlineData("signatures")]
    [InlineData("definitions")]
    [InlineData("members")]
    [InlineData("types")]
    [InlineData("methods")]
    [InlineData("stackTypes")]
    [InlineData("typeNames")]
    public void MissingCollaboratorsReportTheExactParameter(string parameter)
    {
        var fixture = new Fixture();
        var exception = Assert.Throws<ArgumentNullException>(() => new UnsafeAccessorTargetResolver(
            parameter == "signatures" ? null! : fixture,
            parameter == "definitions" ? null! : fixture,
            parameter == "members" ? null! : fixture.Registry,
            parameter == "types" ? null! : fixture,
            parameter == "methods" ? null! : fixture,
            parameter == "stackTypes" ? null! : fixture,
            parameter == "typeNames" ? null! : fixture));
        Assert.Equal(parameter, exception.ParamName);
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public void InvalidMemberResolverResultCannotProduceAnExecutableBinding()
    {
        var fixture = new Fixture { Match = null! };
        Assert.Throws<InvalidOperationException>(() => fixture.Create().Resolve(fixture.Accessor()));
        Assert.Single(fixture.Requests);
        Assert.Empty(fixture.ExceptionNames);
    }

    [Fact]
    public void FailureSelectsTheParameterlessInstanceConstructorAmongOtherMembers()
    {
        var fixture = new Fixture
        {
            Match = new UnsafeAccessorMemberMatch.Failure(UnsafeAccessorFailure.MissingMethod),
            IncludeExceptionDecoys = true,
        };
        var result = Assert.IsType<UnsafeAccessorBinding.Failure>(fixture.Create().Resolve(fixture.Accessor()));
        Assert.Equal("System.MissingMethodException", result.ExceptionConstructor.DeclaringType.FullName);
        Assert.Equal(".ctor", result.ExceptionConstructor.Definition.Name);
        Assert.False(result.ExceptionConstructor.Definition.IsStatic);
        Assert.Empty(result.ExceptionConstructor.Signature.ParameterTypes);
    }

    private static MethodInstanceModel Change(MethodInstanceModel method, UnsafeAccessorDeclaration declaration) => method with { Definition = method.Definition with { UnsafeAccessor = declaration } };
    private static MethodSignature<CliTypeIdentity> Signature(CliTypeIdentity result, params CliTypeIdentity[] parameters) => new(new SignatureHeader(0), result, parameters.Length, 0, [.. parameters]);

    private sealed class Fixture : IUnsafeAccessorSignatureReader, ITypeDefinitionResolver, IUnsafeAccessorMemberResolver,
        ITypeFinder, IMethodRepository, IMetadataStackTypeResolver, IUnsafeAccessorTypeNameResolver
    {
        internal Dictionary<string, TypeDefinitionModel> NamedTypes { get; } = [];
        internal List<(string, AssemblyIdentity)> TypeNameRequests { get; } = [];
        private readonly Dictionary<EntityKey, MethodDefinitionModel> _exceptionConstructors = [];
        internal TypeDefinitionModel OwnerDefinition { get; set; } = new(new(Assembly, 0x02000001), "Test", "Owner", false, [], []);
        internal MethodDefinitionModel Target { get; } = new(new(Assembly, 0x06000002), new(Assembly, 0x02000001), "Target", false, MethodSignatureModel.Create(CliValueKind.Void), 1);
        internal MethodSignature<CliTypeIdentity> Signature { get; set; } = UnsafeAccessorTargetResolverTests.Signature(Void, Owner);
        internal UnsafeAccessorMemberMatch Match { get; set; }
        internal List<UnsafeAccessorMemberRequest> Requests { get; } = [];
        internal List<string> ExceptionNames { get; } = [];
        internal CliTypeIdentity? ResolvedOwner { get; private set; }
        internal int StackResolutions { get; private set; }
        internal bool IncludeExceptionDecoys { get; init; }
        internal Fixture() => Match = new UnsafeAccessorMemberMatch.Method(Target);
        internal ImmutableDictionary<UnsafeAccessorMemberKind, IUnsafeAccessorMemberResolver> Registry =>
            Enum.GetValues<UnsafeAccessorMemberKind>().ToImmutableDictionary(kind => kind, _ => (IUnsafeAccessorMemberResolver)this);
        internal IUnsafeAccessorTargetResolver Create(ImmutableDictionary<UnsafeAccessorMemberKind, IUnsafeAccessorMemberResolver>? registry = null) =>
            ThroughContract(new UnsafeAccessorTargetResolver(this, this, registry ?? Registry, this, this, this, this));
        private static IUnsafeAccessorTargetResolver ThroughContract(IUnsafeAccessorTargetResolver resolver) => resolver;
        internal MethodInstanceModel Accessor(UnsafeAccessorMemberKind kind = UnsafeAccessorMemberKind.Method)
        {
            var definition = new MethodDefinitionModel(new(Assembly, 0x06000001), OwnerDefinition.Key, "Access", true,
                new(Signature.ReturnType, Signature.ParameterTypes), 0)
            { UnsafeAccessor = new((int)kind, null, false, false) };
            return new(definition, Owner, [], definition.Signature);
        }
        public MethodSignature<CliTypeIdentity> Read(MethodDefinitionModel method) => Signature;
        public CliTypeIdentity Read(FieldDefinitionModel field) => throw new NotSupportedException();
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) { ResolvedOwner = identity; return OwnerDefinition; }
        public UnsafeAccessorMemberMatch Resolve(UnsafeAccessorMemberRequest request) { Requests.Add(request); return Match; }
        public TypeDefinitionModel FindType(string fullName)
        {
            ExceptionNames.Add(fullName);
            var owner = new EntityKey(Assembly, 0x02000010 + ExceptionNames.Count);
            var key = new EntityKey(Assembly, 0x06000010 + ExceptionNames.Count);
            _exceptionConstructors.Add(key, new(key, owner, ".ctor", false, MethodSignatureModel.Create(CliValueKind.Void), 1));
            var candidates = ImmutableArray.CreateBuilder<EntityKey>();
            candidates.Add(key);
            if (IncludeExceptionDecoys)
            {
                foreach (var (name, isStatic, signature) in new[]
                {
                    ("Other", false, MethodSignatureModel.Create(CliValueKind.Void)),
                    (".ctor", true, MethodSignatureModel.Create(CliValueKind.Void)),
                    (".ctor", false, MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4)),
                })
                {
                    var candidate = new EntityKey(Assembly, key.MetadataToken + candidates.Count);
                    _exceptionConstructors.Add(candidate, new(candidate, owner, name, isStatic, signature, 1));
                    candidates.Add(candidate);
                }
            }
            var split = fullName.LastIndexOf('.');
            return new(owner, fullName[..split], fullName[(split + 1)..], false, [], candidates.ToImmutable());
        }
        public MethodDefinitionModel GetMethod(EntityKey key) => _exceptionConstructors[key];
        public TypeDefinitionModel? Resolve(string typeName, AssemblyIdentity accessorAssembly)
        {
            TypeNameRequests.Add((typeName, accessorAssembly));
            return NamedTypes.GetValueOrDefault(typeName);
        }
        public CliTypeIdentity Resolve(CliTypeIdentity type) => throw new NotSupportedException();
        public MethodSignatureModel Resolve(MethodSignatureModel signature) => throw new NotSupportedException();
        public CliTypeIdentity Resolve(CliTypeIdentity type, CliGenericContext genericContext)
        {
            StackResolutions++;
            return type.Substitute(genericContext.TypeArguments, genericContext.MethodArguments);
        }
        public MethodSignatureModel Resolve(MethodSignatureModel signature, CliGenericContext genericContext)
        {
            StackResolutions++;
            return signature.Substitute(genericContext.TypeArguments, genericContext.MethodArguments);
        }
    }
}
