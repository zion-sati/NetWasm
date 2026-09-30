using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataPropertyAccessorResolverTests
{
    private static readonly AssemblyIdentity Assembly = new("Fixture");
    private static readonly EntityKey DeclaringType = new(Assembly, 0x02000001);
    private static readonly EntityKey Getter = new(Assembly, 0x06000001);
    private static readonly EntityKey Setter = new(Assembly, 0x06000002);
    private static readonly CliTypeIdentity OpenDeclaringType = CliTypeIdentity.Named(
        Assembly,
        "Fixture",
        "Owner`1",
        isValueType: false);

    [Fact]
    public void ResolveClosesPropertyAndSiblingAccessorFromKnownGetter()
    {
        var typeArgument = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var closedDeclaringType = CliTypeIdentity.GenericInstantiation(
            OpenDeclaringType,
            [typeArgument]);
        var getter = Method(
            Getter,
            "get_Value",
            CliTypeIdentity.GenericParameter(method: false, 0));
        var setter = Method(
            Setter,
            "set_Value",
            CliTypeIdentity.FromStackKind(CliValueKind.Void),
            CliTypeIdentity.GenericParameter(method: false, 0));
        var getterInstance = new MethodInstanceModel(
            getter,
            closedDeclaringType,
            [],
            getter.Signature.Substitute([typeArgument]));
        var methods = new RecordingMethodInstanceResolver(getter, setter);
        var resolver = new MetadataPropertyAccessorResolver(
            [new PropertyDefinitionModel(
                new EntityKey(Assembly, 0x17000001),
                DeclaringType,
                "Value",
                CliTypeIdentity.GenericParameter(method: false, 0),
                [CliTypeIdentity.GenericParameter(method: false, 0)],
                Getter,
                Setter)],
            methods);

        var property = resolver.Resolve(getterInstance);

        Assert.NotNull(property);
        Assert.Equal("primitive:i4", property.PropertyType.CanonicalName);
        Assert.Equal(closedDeclaringType, property.DeclaringType);
        Assert.Equal(
            "primitive:i4",
            Assert.Single(property.IndexParameterTypes).CanonicalName);
        Assert.Same(getterInstance, property.Getter);
        Assert.Equal(Setter, property.Setter!.Definition.Key);
        Assert.Equal("primitive:i4", property.Setter.Signature.ParameterSignatureTypes[0].CanonicalName);
        Assert.Equal(Setter, Assert.Single(methods.ResolvedMethods));
    }

    [Fact]
    public void ResolveReturnsNullForMethodWithoutPropertySemantics()
    {
        var method = Method(
            Getter,
            "Ordinary",
            CliTypeIdentity.FromStackKind(CliValueKind.Void));
        var instance = new MethodInstanceModel(method, OpenDeclaringType, [], method.Signature);
        var resolver = new MetadataPropertyAccessorResolver(
            [],
            new RecordingMethodInstanceResolver(method));

        Assert.Null(resolver.Resolve(instance));
    }

    [Fact]
    public void ResolvePreservesMissingSiblingAccessor()
    {
        var getter = Method(
            Getter,
            "get_Value",
            CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var instance = new MethodInstanceModel(
            getter,
            OpenDeclaringType,
            [],
            getter.Signature);
        var resolver = new MetadataPropertyAccessorResolver(
            [Property("Value", 1)],
            new RecordingMethodInstanceResolver(getter));

        var property = resolver.Resolve(instance);

        Assert.NotNull(property);
        Assert.Same(instance, property.Getter);
        Assert.Null(property.Setter);
    }

    [Fact]
    public void ConstructorRejectsAnAccessorAssociatedWithMultipleProperties()
    {
        var method = Method(
            Getter,
            "get_Value",
            CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var properties = ImmutableArray.Create(
            Property("First", 1),
            Property("Second", 2));

        var exception = Assert.Throws<CompilerException>(() =>
            new MetadataPropertyAccessorResolver(
                properties,
                new RecordingMethodInstanceResolver(method)));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
    }

    [Fact]
    public void ConstructorAndResolveRejectNullDependencies()
    {
        var method = Method(
            Getter,
            "get_Value",
            CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var resolver = new MetadataPropertyAccessorResolver(
            [],
            new RecordingMethodInstanceResolver(method));

        Assert.Throws<ArgumentNullException>(() =>
            new MetadataPropertyAccessorResolver([], null!));
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(null!));
    }

    private static PropertyDefinitionModel Property(string name, int row) => new(
        new EntityKey(Assembly, 0x17000000 + row),
        DeclaringType,
        name,
        CliTypeIdentity.FromStackKind(CliValueKind.I4),
        [],
        Getter,
        null);

    private static MethodDefinitionModel Method(
        EntityKey key,
        string name,
        CliTypeIdentity returnType,
        params CliTypeIdentity[] parameterTypes) => new(
            key,
            DeclaringType,
            name,
            IsStatic: false,
            MethodSignatureModel.Create(returnType, parameterTypes),
            RelativeVirtualAddress: 1);

    private sealed class RecordingMethodInstanceResolver(
        params MethodDefinitionModel[] methods) : IMethodInstanceResolver
    {
        private readonly ImmutableDictionary<EntityKey, MethodDefinitionModel> _methods =
            methods.ToImmutableDictionary(method => method.Key);
        private readonly List<EntityKey> _resolvedMethods = [];

        public ImmutableArray<EntityKey> ResolvedMethods => [.. _resolvedMethods];

        public MethodInstanceModel ResolveMethodInstance(
            AssemblyIdentity source,
            int metadataToken,
            string methodDisplayName,
            int ilOffset,
            CliGenericContext? genericContext = null)
        {
            var key = new EntityKey(source, metadataToken);
            _resolvedMethods.Add(key);
            var definition = _methods[key];
            var context = (genericContext ?? CliGenericContext.Empty).Normalize();
            var declaringType = context.TypeArguments.IsEmpty
                ? OpenDeclaringType
                : CliTypeIdentity.GenericInstantiation(
                    OpenDeclaringType,
                    context.TypeArguments);
            return new MethodInstanceModel(
                definition,
                declaringType,
                [],
                definition.Signature.Substitute(context.TypeArguments));
        }
    }
}
