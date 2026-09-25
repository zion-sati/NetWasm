using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataTypeClassifierTests
{
    [Fact]
    public void IsDelegateTypeRecognizesDirectInheritedAndGenericTraversal()
    {
        var key = new EntityKey(new("Test"), 1);
        var direct = Identity("System", "Delegate");
        Assert.True(Classify(key, direct, [Definition(direct)], []));

        var derived = Identity("Test", "Derived");
        var multicast = Identity("System", "MulticastDelegate");
        Assert.True(Classify(
            key,
            derived,
            [Definition(derived), Definition(multicast)],
            [(derived, multicast)]));

        var unrelated = Identity("Test", "Unrelated");
        Assert.False(Classify(key, unrelated, [Definition(unrelated)], []));

        var generic = Identity("Test", "Generic`1");
        var baseTypes = new StubBaseTypeResolver([]);
        var classifier = new MetadataTypeClassifier(
            new StubTypeIdentityResolver(generic),
            new StubTypeDefinitionResolver([Definition(generic) with { GenericArity = 1 }]),
            baseTypes);
        Assert.False(((ITypeClassifier)classifier).IsDelegateType(key));
        Assert.Equal(CliTypeShape.GenericInstantiation, baseTypes.LastType!.Shape);
    }

    private static bool Classify(
        EntityKey key,
        CliTypeIdentity initial,
        TypeDefinitionModel[] definitions,
        (CliTypeIdentity Type, CliTypeIdentity BaseType)[] baseTypes)
    {
        var classifier = new MetadataTypeClassifier(
            new StubTypeIdentityResolver(initial),
            new StubTypeDefinitionResolver(definitions),
            new StubBaseTypeResolver(baseTypes));
        return ((ITypeClassifier)classifier).IsDelegateType(key);
    }

    private static CliTypeIdentity Identity(string @namespace, string name) =>
        CliTypeIdentity.Named(new("Test"), @namespace, name, false);

    private static TypeDefinitionModel Definition(CliTypeIdentity identity) => new(
        new(new("Test"), identity.FullName!.GetHashCode(StringComparison.Ordinal)),
        identity.FullName[..identity.FullName.LastIndexOf('.')],
        identity.FullName[(identity.FullName.LastIndexOf('.') + 1)..],
        false,
        [],
        []);

    private sealed class StubTypeIdentityResolver(CliTypeIdentity result) :
        ITypeIdentityResolver
    {
        public CliTypeIdentity GetTypeIdentity(EntityKey type) => result;
    }

    private sealed class StubTypeDefinitionResolver(TypeDefinitionModel[] definitions) :
        ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            definitions.Single(definition => definition.FullName == identity.FullName);
    }

    private sealed class StubBaseTypeResolver(
        (CliTypeIdentity Type, CliTypeIdentity BaseType)[] baseTypes) :
        IMetadataIdentityBaseTypeResolver
    {
        internal CliTypeIdentity? LastType { get; private set; }

        public CliTypeIdentity? GetBaseType(CliTypeIdentity type)
        {
            LastType = type;
            return baseTypes
                .Where(pair => pair.Type.FullName == type.FullName)
                .Select(pair => pair.BaseType)
                .SingleOrDefault();
        }
    }
}
