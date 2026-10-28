using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class MethodDependencyResolverTests
{
    private static readonly AssemblyIdentity Assembly = new("Tests");
    private static readonly EntityKey TypeKey = new(Assembly, 1);
    private static readonly EntityKey MethodKey = new(Assembly, 2);
    private static readonly CliTypeIdentity Type = CliTypeIdentity.Named(
        Assembly,
        "Tests",
        "Fixture",
        isValueType: false);
    private static readonly MethodDefinitionModel Method = new(
        MethodKey,
        TypeKey,
        "Run",
        true,
        MethodSignatureModel.Create(CliValueKind.Void),
        0);
    private static readonly MethodInstanceModel Instance = new(
        Method,
        Type,
        [],
        Method.Signature);

    [Fact]
    public void CalledMethodResolverHandlesEveryOperandContract()
    {
        var methods = new UnitMethodRepository();
        var instances = new UnitMethodInstanceResolver();
        var symbols = new UnitSymbolFormatter();
        var resolver = new CalledMethodResolver(methods, instances, symbols);

        Assert.Same(
            Instance,
            resolver.Resolve(Instruction(
                CilOperation.Call,
                new CilOperand.MethodInstance(Instance))));
        Assert.Same(
            Instance,
            resolver.Resolve(Instruction(
                CilOperation.CallVirtual,
                new CilOperand.Entity(MethodKey))));
        Assert.Null(resolver.Resolve(Instruction(CilOperation.Add)));
        Assert.Null(resolver.Resolve(Instruction(
            CilOperation.Call,
            new CilOperand.ConstantI4(1))));
    }

    [Fact]
    public void TypeOperandResolverHandlesEveryOperandContract()
    {
        var resolver = new TypeOperandResolver(new UnitTypeIdentityResolver());

        Assert.Same(
            Type,
            resolver.Resolve(Instruction(
                CilOperation.Box,
                new CilOperand.TypeIdentity(Type)), methodInstance: null));
        Assert.Same(
            Type,
            resolver.Resolve(Instruction(
                CilOperation.Box,
                new CilOperand.Entity(TypeKey)), methodInstance: null));
        var error = Assert.Throws<InvalidOperationException>(() =>
            resolver.Resolve(Instruction(CilOperation.Box), methodInstance: null));

        Assert.Contains("no type operand", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BaseTypeResolverMapsArraysAndDelegatesOtherTypes()
    {
        var arrayType = CliTypeIdentity.SzArray(Type);
        var baseType = CliTypeIdentity.Named(
            Assembly,
            "Tests",
            "Base",
            false);
        var resolver = new BaseTypeResolver(
            new FixedTypeFinder(),
            new FixedTypeIdentityResolver(),
            new FixedBaseTypeIdentityResolver(baseType));

        Assert.Equal(baseType, resolver.Resolve(Type));
        Assert.Equal(Type, resolver.Resolve(arrayType));
    }

    [Fact]
    public void TypeRelationshipClassifierRejectsMismatchedVarianceMetadata()
    {
        var definition = CliTypeIdentity.Named(
            Assembly,
            "Tests",
            "Box`1",
            false);
        var candidate = CliTypeIdentity.GenericInstantiation(
            definition,
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var target = CliTypeIdentity.GenericInstantiation(
            definition,
            [CliTypeIdentity.Primitive("i8", CliValueKind.I8)]);
        var classifier = new TypeRelationshipClassifier(
            new UnusedTypeFinder(),
            new FixedTypeDefinitionResolver([]),
            new UnusedTypeIdentityResolver(),
            new EmptyImplementedInterfaceResolver(),
            new NullBaseTypeResolver());

        Assert.False(classifier.Classify(candidate, target).RequiresVariantMethodResolution);
    }

    [Fact]
    public void TypeRelationshipClassifierComparesArrayShapesStructurally()
    {
        var classifier = new TypeRelationshipClassifier(
            new UnusedTypeFinder(),
            new FixedTypeDefinitionResolver([]),
            new UnusedTypeIdentityResolver(),
            new EmptyImplementedInterfaceResolver(),
            new NullBaseTypeResolver());
        var element = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var same = CliTypeIdentity.SzArray(element);
        var different = CliTypeIdentity.SzArray(
            CliTypeIdentity.Primitive("i8", CliValueKind.I8));
        var rankTwo = CliTypeIdentity.Array(element, 2);

        Assert.True(classifier.Classify(same, same).IsAssignmentCompatible);
        Assert.False(classifier.Classify(same, different).IsAssignmentCompatible);
        Assert.False(classifier.Classify(same, rankTwo).IsAssignmentCompatible);
        Assert.False(classifier.Classify(Type, same).IsAssignmentCompatible);
    }

    private static CilInstruction Instruction(
        CilOperation operation,
        CilOperand? operand = null) => new(
            4,
            5,
            operation,
            operand ?? new CilOperand.None());

    private sealed class UnitMethodRepository : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) => Method;
    }

    private sealed class UnitMethodInstanceResolver : IMethodInstanceResolver
    {
        public MethodInstanceModel ResolveMethodInstance(
            AssemblyIdentity source,
            int metadataToken,
            string methodDisplayName,
            int ilOffset,
            CliGenericContext? genericContext = null) => Instance;
    }

    private sealed class UnitSymbolFormatter : ISymbolFormatter
    {
        public string Format(EntityKey key) => key.ToString();

        public string Format(MethodDefinitionModel method) =>
            "Tests.Fixture::Run";
    }

    private sealed class UnitTypeIdentityResolver : ITypeIdentityResolver
    {
        public CliTypeIdentity GetTypeIdentity(EntityKey type) => Type;
    }

    private sealed class FixedTypeFinder : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName) => new(
            TypeKey,
            "Tests",
            "Array",
            false,
            [],
            []);
    }

    private sealed class FixedTypeIdentityResolver : ITypeIdentityResolver
    {
        public CliTypeIdentity GetTypeIdentity(EntityKey type) => Type;
    }

    private sealed class FixedBaseTypeIdentityResolver(CliTypeIdentity baseType) : IBaseTypeIdentityResolver
    {
        public CliTypeIdentity? GetBaseTypeIdentity(CliTypeIdentity type) => baseType;
    }

    private sealed class FixedTypeDefinitionResolver(
        System.Collections.Immutable.ImmutableArray<CliGenericVariance> variances) :
        ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => new(
            identity.Shape == CliTypeShape.GenericInstantiation
                ? TypeKey
                : new EntityKey(Assembly, identity.CanonicalName.GetHashCode()),
            "Tests",
            "Box`1",
            false,
            [],
            [])
        {
            GenericParameterVariances = variances,
        };
    }

    private sealed class UnusedTypeFinder : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName) =>
            throw new InvalidOperationException();
    }

    private sealed class UnusedTypeIdentityResolver : ITypeIdentityResolver
    {
        public CliTypeIdentity GetTypeIdentity(EntityKey type) =>
            throw new InvalidOperationException();
    }

    private sealed class EmptyImplementedInterfaceResolver : IImplementedInterfaceResolver
    {
        public System.Collections.Immutable.ImmutableArray<CliTypeIdentity> GetInterfaces(
            CliTypeIdentity type) => [];
    }

    private sealed class NullBaseTypeResolver : IBaseTypeResolver
    {
        public CliTypeIdentity? Resolve(CliTypeIdentity type) => null;
    }
}
