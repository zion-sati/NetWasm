using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class BaseTypeResolverTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ElementModifiersHaveNoBaseAndSkipAllCollaborators(
        bool layoutPhase,
        bool unmanaged)
    {
        var dependencies = new ResolutionDependencies(throwOnCall: true);
        var baseTypes = new RecordingBaseTypes(null, throwOnCall: true);
        var resolver = Create(layoutPhase, dependencies, baseTypes);
        var element = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var modifier = unmanaged
            ? CliTypeIdentity.UnmanagedPointer(element)
            : CliTypeIdentity.ManagedByReference(element);

        Assert.Null(resolver.Resolve(modifier));
        Assert.Equal(0, dependencies.Calls);
        Assert.Empty(baseTypes.Requests);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OpenGenericParametersHaveNoRuntimeBaseType(
        bool layoutPhase,
        bool methodParameter)
    {
        var dependencies = new ResolutionDependencies(throwOnCall: true);
        var baseTypes = new RecordingBaseTypes(null, throwOnCall: true);
        var resolver = Create(layoutPhase, dependencies, baseTypes);
        var parameter = CliTypeIdentity.GenericParameter(
            methodParameter,
            index: 0);

        Assert.Null(resolver.Resolve(parameter));
        Assert.Equal(0, dependencies.Calls);
        Assert.Empty(baseTypes.Requests);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ArraysResolveSystemArrayWithoutMetadataBaseLookup(
        bool layoutPhase,
        bool ranked)
    {
        var arrayDefinition = Type("System", "Array", token: 1);
        var arrayIdentity = CliTypeIdentity.FromDefinition(arrayDefinition);
        var dependencies = new ResolutionDependencies(
            arrayDefinition,
            arrayIdentity);
        var baseTypes = new RecordingBaseTypes(null, throwOnCall: true);
        var resolver = Create(layoutPhase, dependencies, baseTypes);
        var element = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var array = ranked
            ? CliTypeIdentity.Array(element, rank: 2)
            : CliTypeIdentity.SzArray(element);

        Assert.Equal(arrayIdentity, resolver.Resolve(array));
        Assert.Equal(2, dependencies.Calls);
        Assert.Empty(baseTypes.Requests);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void DefinitionBackedIdentitiesDelegateWithoutShapeChanges(
        bool layoutPhase,
        bool constructed,
        bool nullResult)
    {
        var definition = Type("Tests", "Box`1", token: 2) with
        {
            GenericArity = 1,
        };
        var named = CliTypeIdentity.FromDefinition(definition);
        var input = constructed
            ? CliTypeIdentity.GenericInstantiation(
                named,
                [CliTypeIdentity.Primitive("i4", CliValueKind.I4)])
            : named;
        var expected = nullResult
            ? null
            : CliTypeIdentity.Named(
                new AssemblyIdentity("Core"),
                "System",
                "Object",
                isValueType: false);
        var dependencies = new ResolutionDependencies(throwOnCall: true);
        var baseTypes = new RecordingBaseTypes(expected);
        var resolver = Create(layoutPhase, dependencies, baseTypes);

        Assert.Equal(expected, resolver.Resolve(input));
        Assert.Equal([input], baseTypes.Requests);
        Assert.Equal(0, dependencies.Calls);
    }

    private static IBaseTypeResolver Create(
        bool layoutPhase,
        ResolutionDependencies dependencies,
        RecordingBaseTypes baseTypes) => layoutPhase
            ? new LayoutBaseTypeResolver(dependencies, dependencies, baseTypes)
            : new BaseTypeResolver(dependencies, dependencies, baseTypes);

    private static TypeDefinitionModel Type(
        string typeNamespace,
        string name,
        int token) => new(
        new EntityKey(new AssemblyIdentity("Tests"), token),
        typeNamespace,
        name,
        IsValueType: false,
        [],
        []);

    private sealed class ResolutionDependencies : ITypeFinder, ITypeIdentityResolver
    {
        private readonly TypeDefinitionModel? _definition;
        private readonly CliTypeIdentity? _identity;
        private readonly bool _throwOnCall;

        internal ResolutionDependencies(bool throwOnCall)
        {
            _throwOnCall = throwOnCall;
        }

        internal ResolutionDependencies(
            TypeDefinitionModel definition,
            CliTypeIdentity identity)
        {
            _definition = definition;
            _identity = identity;
        }

        internal int Calls { get; private set; }

        public TypeDefinitionModel FindType(string fullName)
        {
            Calls++;
            if (_throwOnCall)
            {
                throw new InvalidOperationException("Type lookup must not run.");
            }
            Assert.Equal("System.Array", fullName);
            return _definition!;
        }

        public CliTypeIdentity GetTypeIdentity(EntityKey type)
        {
            Calls++;
            if (_throwOnCall)
            {
                throw new InvalidOperationException("Identity lookup must not run.");
            }
            Assert.Equal(_definition!.Key, type);
            return _identity!;
        }
    }

    private sealed class RecordingBaseTypes(
        CliTypeIdentity? result,
        bool throwOnCall = false) :
        IBaseTypeIdentityResolver,
        IMetadataIdentityBaseTypeResolver
    {
        internal List<CliTypeIdentity> Requests { get; } = [];

        public CliTypeIdentity? GetBaseTypeIdentity(CliTypeIdentity type) =>
            Resolve(type);

        public CliTypeIdentity? GetBaseType(CliTypeIdentity type) => Resolve(type);

        private CliTypeIdentity? Resolve(CliTypeIdentity type)
        {
            Requests.Add(type);
            return throwOnCall
                ? throw new InvalidOperationException("Base lookup must not run.")
                : result;
        }
    }
}
