using NetWasm.Compiler.Core.GenericConstraints;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class RequiredTypeDependencyValidatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SealedBaseIsRejectedEvenWhenValidatedEarlierAsAnIndependentRoot(bool baseVisitedFirst)
    {
        var derived = Named("Derived");
        var baseType = Named("Base");
        var definitions = new Definitions([derived, baseType]);
        definitions.Sealed.Add(baseType);
        var validator = Assert.IsAssignableFrom<IRequiredTypeDependencyValidator>(new RequiredTypeDependencyValidator(
            definitions, new Bases(new() { [derived] = baseType }), new Interfaces([]), new NoGenericConstraints()));

        var failure = Assert.Throws<CompilerException>(() => validator.Validate(
            baseVisitedFirst ? [derived, baseType] : [baseType, derived]));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, failure.Diagnostic.Code);
        Assert.Equal(derived.CanonicalName, failure.Diagnostic.Context!.InstanceIdentity);
        Assert.Equal(CompilerDependencyKind.BaseType, Assert.Single(failure.Diagnostic.DependencyChain).Kind);
        Assert.Equal(2, definitions.Calls.Count);
    }

    [Fact]
    public void SealedTypesRemainValidAsOrdinaryRequirements()
    {
        var type = Named("Sealed");
        var definitions = new Definitions([type]);
        definitions.Sealed.Add(type);
        var validator = Assert.IsAssignableFrom<IRequiredTypeDependencyValidator>(new RequiredTypeDependencyValidator(
            definitions, new Bases([]), new Interfaces([]), new NoGenericConstraints()));
        validator.Validate([type]);
        Assert.Equal(type, Assert.Single(definitions.Calls));
    }

    [Fact]
    public void ReportsRequiringBaseAndInterfaceChainInRootOrder()
    {
        var root = Named("Root");
        var baseType = Named("Base");
        var missing = Named("Missing");
        var validator = Assert.IsAssignableFrom<IRequiredTypeDependencyValidator>(
            new RequiredTypeDependencyValidator(new Definitions([root, baseType]),
                new Bases(new() { [root] = baseType }),
                new Interfaces(new() { [baseType] = [missing] }), new NoGenericConstraints()));

        var failure = Assert.Throws<CompilerException>(() => validator.Validate([root]));

        Assert.Collection(failure.Diagnostic.DependencyChain,
            step =>
            {
                Assert.Equal(root.CanonicalName, step.Source.InstanceIdentity);
                Assert.Equal(CompilerDependencyKind.BaseType, step.Kind);
            },
            step =>
            {
                Assert.Equal(baseType.CanonicalName, step.Source.InstanceIdentity);
                Assert.Equal(CompilerDependencyKind.Interface, step.Kind);
            });
        Assert.Equal(baseType.CanonicalName, failure.Diagnostic.Context!.InstanceIdentity);
    }

    [Fact]
    public void ValidatesBaseInterfaceAndGenericArgumentClosureOnce()
    {
        var root = Named("Root");
        var baseType = Named("Base");
        var argument = Named("Argument");
        var implemented = CliTypeIdentity.GenericInstantiation(Named("IContract`1"), [argument]);
        var inherited = Named("IInherited");
        var definitions = new Definitions([root, baseType, argument, implemented, inherited]);
        var bases = new Bases(new() { [root] = baseType });
        var interfaces = new Interfaces(new()
        {
            [baseType] = [implemented],
            [implemented] = [inherited],
            [inherited] = [inherited]
        });
        var validator = Assert.IsAssignableFrom<IRequiredTypeDependencyValidator>(
            new RequiredTypeDependencyValidator(definitions, bases, interfaces, new NoGenericConstraints()));

        validator.Validate([root, root]);

        Assert.Equal(5, definitions.Calls.Count);
        Assert.Equal(5, definitions.Calls.Distinct().Count());
        Assert.Contains(argument, definitions.Calls);
        Assert.Contains(inherited, definitions.Calls);
    }

    [Theory]
    [InlineData("interface")]
    [InlineData("base")]
    [InlineData("argument")]
    public void RejectsMissingRequiredDependency(string role)
    {
        var root = Named("Root");
        var missing = Named("Missing");
        var implemented = CliTypeIdentity.GenericInstantiation(Named("IContract`1"), [missing]);
        var definitions = new Definitions([root, implemented]);
        var bases = new Bases(role == "base" ? new() { [root] = missing } : []);
        var interfaces = new Interfaces(role switch
        {
            "interface" => new() { [root] = [missing] },
            "argument" => new() { [root] = [implemented] },
            _ => []
        });
        var validator = Assert.IsAssignableFrom<IRequiredTypeDependencyValidator>(
            new RequiredTypeDependencyValidator(definitions, bases, interfaces, new NoGenericConstraints()));

        var error = Assert.Throws<CompilerException>(() => validator.Validate([root]));

        Assert.Equal(DiagnosticCode.AssemblyResolution, error.Diagnostic.Code);
        Assert.Contains(missing, definitions.Calls);
        var context = Assert.IsType<CompilerDiagnosticContext>(error.Diagnostic.Context);
        Assert.Equal(new AssemblyIdentity("Test"), context.Source.Assembly);
        Assert.Equal(0x02000001, context.Source.MetadataToken);
        Assert.IsType<CompilerException>(error.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreservesExistingContextOrUnattributedRootFailure(bool hasContext)
    {
        var root = Named("Root");
        var context = hasContext
            ? new CompilerDiagnosticContext(new(new("Original"), 0x02000002), "Original.Type")
            : null;
        var failure = new CompilerException(new(DiagnosticCode.AssemblyResolution, "Missing root")
        {
            Context = context
        });
        var validator = new RequiredTypeDependencyValidator(
            new FailingDefinitions(failure), new Bases([]), new Interfaces([]), new NoGenericConstraints());

        var actual = Assert.Throws<CompilerException>(() => validator.Validate([root]));

        Assert.Same(failure, actual);
        Assert.Same(context, actual.Diagnostic.Context);
    }

    // A failure that already names its dependency chain keeps it; the type that hit it becomes its context.
    [Fact]
    public void AFailureKeepsTheDependencyChainItAlreadyHas()
    {
        var root = Named("Root");
        CompilerDependencyStep[] chain = [new(new(new(new("Elsewhere"), 0x02000003)), CompilerDependencyKind.TypeArgument)];
        var failure = new CompilerException(new(DiagnosticCode.UnsupportedMetadata, "constraint") { DependencyChain = [.. chain] });
        var validator = new RequiredTypeDependencyValidator(new Definitions([root]), new Bases([]), new Interfaces([]),
            new FailingConstraints(failure));

        var actual = Assert.Throws<CompilerException>(() => validator.Validate([root]));

        Assert.Equal(root.CanonicalName, actual.Diagnostic.Context!.InstanceIdentity);
        Assert.Equal(chain, actual.Diagnostic.DependencyChain);
    }

    [Fact]
    public void AttributesFailureWhileReadingBaseToResolvedOwner()
    {
        var root = Named("Root");
        var failure = new CompilerException(new(DiagnosticCode.AssemblyResolution, "Missing base"));
        var validator = new RequiredTypeDependencyValidator(
            new Definitions([root]), new FailingBases(failure), new Interfaces([]), new NoGenericConstraints());

        var actual = Assert.Throws<CompilerException>(() => validator.Validate([root]));

        Assert.Same(failure, actual.InnerException);
        Assert.Equal(new EntityKey(new("Test"), 0x02000001), actual.Diagnostic.Context!.Source);
    }

    private sealed class FailingDefinitions(CompilerException failure) : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => throw failure;
    }

    private sealed class FailingBases(CompilerException failure) : IBaseTypeIdentityResolver
    {
        public CliTypeIdentity? GetBaseTypeIdentity(CliTypeIdentity identity) => throw failure;
    }

    [Fact]
    public void ValidatesWrappedTypeAndIgnoresPrimitiveAndGenericParameters()
    {
        var element = Named("Element");
        var definitions = new Definitions([element]);
        var validator = Assert.IsAssignableFrom<IRequiredTypeDependencyValidator>(
            new RequiredTypeDependencyValidator(definitions, new Bases([]), new Interfaces([]), new NoGenericConstraints()));

        validator.Validate([
            CliTypeIdentity.SzArray(element),
            CliTypeIdentity.Array(element, 2),
            CliTypeIdentity.ManagedByReference(element),
            CliTypeIdentity.UnmanagedPointer(element),
            CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            CliTypeIdentity.GenericParameter(false, 0),
            CliTypeIdentity.GenericParameter(true, 0)
        ]);

        Assert.Equal(element, Assert.Single(definitions.Calls));
    }

    [Fact]
    public void EmptyRootsDoNotValidateUnusedDefinitions()
    {
        var definitions = new Definitions([]);
        var validator = Assert.IsAssignableFrom<IRequiredTypeDependencyValidator>(
            new RequiredTypeDependencyValidator(definitions, new Bases([]), new Interfaces([]), new NoGenericConstraints()));

        validator.Validate([]);

        Assert.Empty(definitions.Calls);
    }

    [Fact]
    public void RejectsNullInputsAndServices()
    {
        var definitions = new Definitions([]);
        var bases = new Bases([]);
        var interfaces = new Interfaces([]);
        Assert.Throws<ArgumentNullException>(() => new RequiredTypeDependencyValidator(null!, bases, interfaces, new NoGenericConstraints()));
        Assert.Throws<ArgumentNullException>(() => new RequiredTypeDependencyValidator(definitions, null!, interfaces, new NoGenericConstraints()));
        Assert.Throws<ArgumentNullException>(() => new RequiredTypeDependencyValidator(definitions, bases, null!, new NoGenericConstraints()));
        var validator = Assert.IsAssignableFrom<IRequiredTypeDependencyValidator>(
            new RequiredTypeDependencyValidator(definitions, bases, interfaces, new NoGenericConstraints()));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));
    }

    private static CliTypeIdentity Named(string name) =>
        CliTypeIdentity.Named(new("Test"), "Test", name, false);

    private sealed class Definitions(IEnumerable<CliTypeIdentity> available) : ITypeDefinitionResolver
    {
        private readonly HashSet<CliTypeIdentity> _available = [.. available];
        public HashSet<CliTypeIdentity> Sealed { get; } = [];
        public List<CliTypeIdentity> Calls { get; } = [];
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
        {
            Calls.Add(identity);
            if (!_available.Contains(identity))
                throw new CompilerException(new(DiagnosticCode.AssemblyResolution, "Missing required type"));
            return new(new(new("Test"), 0x02000001), "Test", "Resolved", false, [], []) { IsSealed = Sealed.Contains(identity) };
        }
    }

    private sealed class Bases(Dictionary<CliTypeIdentity, CliTypeIdentity> values) : IBaseTypeIdentityResolver
    {
        public CliTypeIdentity? GetBaseTypeIdentity(CliTypeIdentity type) => values.GetValueOrDefault(type);
    }

    private sealed class Interfaces(Dictionary<CliTypeIdentity, ImmutableArray<CliTypeIdentity>> values) : IImplementedInterfaceResolver
    {
        public ImmutableArray<CliTypeIdentity> GetInterfaces(CliTypeIdentity type) =>
            values.GetValueOrDefault(type, []);
    }
    private sealed class FailingConstraints(CompilerException failure) : IClosedGenericConstraintValidator
    {
        public void Validate(MethodInstanceModel method) { }
        public void Validate(CliTypeIdentity type) => throw failure;
    }

    private sealed class NoGenericConstraints : IClosedGenericConstraintValidator
    {
        public void Validate(MethodInstanceModel method) { }
        public void Validate(CliTypeIdentity type) { }
    }

}
