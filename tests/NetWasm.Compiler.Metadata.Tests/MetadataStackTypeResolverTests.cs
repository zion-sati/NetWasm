using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataStackTypeResolverTests
{
    private static readonly AssemblyIdentity Assembly = new("Test");

    [Fact]
    public void StrategyRejectsMissingDependenciesAndInput()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new MetadataStackTypeResolver(null!));
        Assert.Throws<ArgumentNullException>(() =>
            ((IMetadataStackTypeResolver)new MetadataStackTypeResolver(
                new FixedTypeDefinitionResolver(Definition(false))))
            .Resolve((CliTypeIdentity)null!));
    }

    [Fact]
    public void StrategyPreservesAlreadyClassifiedAndNonNamedStackKinds()
    {
        var resolver = new FixedTypeDefinitionResolver(Definition(false));

        Assert.Equal(
            CliValueKind.I4,
            Resolve(resolver, CliTypeIdentity.Primitive("i4", CliValueKind.I4)).StackKind);
        Assert.Equal(
            CliValueKind.ValueType,
            Resolve(resolver, CliTypeIdentity.Primitive(
                "opaque",
                CliValueKind.ValueType)).StackKind);
        Assert.Equal(0, resolver.InvocationCount);
    }

    [Fact]
    public void StrategyResolvesNamedEnumsToTheirUnderlyingStackKind()
    {
        var resolver = new FixedTypeDefinitionResolver(Definition(true));

        var original = NamedValueType();
        var resolved = Resolve(resolver, original);

        Assert.Equal(CliValueKind.I4, resolved.StackKind);
        Assert.Equal(original.CanonicalName, resolved.CanonicalName);
        Assert.Equal(original.FullName, resolved.FullName);
        Assert.Equal(original.Assembly, resolved.Assembly);
        Assert.Equal(1, resolver.InvocationCount);
    }

    [Fact]
    public void StrategyPreservesNamedNonEnumValueTypes()
    {
        var resolver = new FixedTypeDefinitionResolver(Definition(false));

        Assert.Equal(
            CliValueKind.ValueType,
            Resolve(resolver, NamedValueType()).StackKind);
        Assert.Equal(1, resolver.InvocationCount);
    }

    [Fact]
    public void StrategyNormalizesEveryEnumInAMethodSignature()
    {
        var resolver = new FixedTypeDefinitionResolver(Definition(true));
        var enumType = NamedValueType();

        var resolved = ((IMetadataStackTypeResolver)new MetadataStackTypeResolver(resolver))
            .Resolve(new MethodSignatureModel(enumType, [enumType]));

        Assert.Equal(CliValueKind.I4, resolved.ReturnType);
        Assert.Equal(CliValueKind.I4, Assert.Single(resolved.ParameterTypes));
        Assert.Equal(CliValueKind.I4, resolved.ReturnSignatureType.StackKind);
        Assert.Equal(
            CliValueKind.I4,
            Assert.Single(resolved.ParameterSignatureTypes).StackKind);
        Assert.Equal(2, resolver.InvocationCount);
    }

    [Fact]
    public void StrategyRejectsMissingMethodSignature()
    {
        var resolver = new MetadataStackTypeResolver(
            new FixedTypeDefinitionResolver(Definition(false)));

        Assert.Throws<ArgumentNullException>(() =>
            ((IMetadataStackTypeResolver)resolver).Resolve((MethodSignatureModel)null!));
    }

    private static CliTypeIdentity Resolve(
        ITypeDefinitionResolver definitions,
        CliTypeIdentity type) =>
        ((IMetadataStackTypeResolver)new MetadataStackTypeResolver(definitions))
        .Resolve(type);

    private static CliTypeIdentity NamedValueType() => CliTypeIdentity.Named(
        Assembly,
        "Test",
        "Value",
        true);

    private static TypeDefinitionModel Definition(bool isEnum) => new(
        new EntityKey(Assembly, 0x02000001),
        "Test",
        "Value",
        true,
        [],
        [])
    {
        IsEnum = isEnum,
        EnumUnderlyingType = CliTypeIdentity.Primitive("i4", CliValueKind.I4),
    };

    private sealed class FixedTypeDefinitionResolver(
        TypeDefinitionModel definition) : ITypeDefinitionResolver
    {
        public int InvocationCount { get; private set; }

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
        {
            InvocationCount++;
            return definition;
        }
    }

    [Fact]
    public void ResolveSubstitutesTypeAndMethodGenericParametersBeforeClassifying()
    {
        var resolver = new MetadataStackTypeResolver(
            new FixedTypeDefinitionResolver(Definition(false)));
        var genericContext = new CliGenericContext(
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)],
            [CliTypeIdentity.Primitive("i8", CliValueKind.I8)]);

        var typeResult = ((IMetadataStackTypeResolver)resolver).Resolve(
            CliTypeIdentity.GenericParameter(method: false, index: 0),
            genericContext);
        var methodResult = ((IMetadataStackTypeResolver)resolver).Resolve(
            CliTypeIdentity.GenericParameter(method: true, index: 0),
            genericContext);

        Assert.Equal(CliValueKind.I4, typeResult.StackKind);
        Assert.Equal(CliValueKind.I8, methodResult.StackKind);
    }

    [Fact]
    public void ResolveSubstitutesMethodSignatureBeforeClassifying()
    {
        var resolver = new MetadataStackTypeResolver(
            new FixedTypeDefinitionResolver(Definition(false)));
        var signature = new MethodSignatureModel(
            CliTypeIdentity.GenericParameter(method: false, index: 0),
            [CliTypeIdentity.GenericParameter(method: true, index: 0)]);
        var genericContext = new CliGenericContext(
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)],
            [CliTypeIdentity.Primitive("i8", CliValueKind.I8)]);

        var result = ((IMetadataStackTypeResolver)resolver).Resolve(
            signature,
            genericContext);

        Assert.Equal(CliValueKind.I4, result.ReturnType);
        Assert.Equal(CliValueKind.I8, Assert.Single(result.ParameterTypes));
    }

}
