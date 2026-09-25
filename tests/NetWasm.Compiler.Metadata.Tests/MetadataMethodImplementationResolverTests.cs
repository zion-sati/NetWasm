using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using Xunit;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataMethodImplementationResolverTests
{
    [Fact]
    public void GetMethodImplementationsDelegatesTypeResolution()
    {
        Action<IMethodImplementationResolver> contract = AssertTypeResolutionFailure;
        contract(
            new MetadataMethodImplementationResolver(
                new UnusedAssemblyResolver(),
                new UnusedMethodReferenceResolver(),
                new FailingTypeDefinitionResolver()));
    }

    private static void AssertTypeResolutionFailure(IMethodImplementationResolver resolver)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => resolver.GetMethodImplementations(
                CliTypeIdentity.Named(
                    MetadataActorTestData.Assembly,
                    "Tests",
                    "Type",
                    isValueType: false)));

        Assert.Equal("type-resolution", exception.Message);
    }

    [Fact]
    public void GetMethodImplementationsMapsExplicitInterfaceMethodsThroughItsInterface()
    {
        using var assets = TestAssets.Create();
        var path = assets.CompileSource(
            "ExplicitImplementation",
            """
            public interface IContract
            {
                void Run();
            }

            public sealed class Implementation : IContract
            {
                void IContract.Run() { }
            }
            """);
        using var assembly = ManagedAssemblyTestFactory.Load(path);
        var definition = assembly.Types.Values.Single(type =>
            type.FullName == "Implementation");
        var source = new MetadataAssemblySnapshot(
            assembly.Identity,
            assembly.Reader,
            assembly.Types,
            assembly.Fields,
            assembly.Methods,
            assembly.Metadata.BaseTypes,
            assembly.Metadata.ImplementedInterfaces);
        var method = assembly.Methods.Values.First(candidate =>
            candidate.DeclaringType == definition.Key);
        var methodInstance = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(assembly.Identity, "", "Implementation", false),
            [],
            method.Signature);
        var resolver = new MetadataMethodImplementationResolver(
            new FixedAssemblyResolver(source),
            new FixedMethodReferenceResolver(methodInstance),
            new FixedTypeDefinitionResolver(definition));
        var identity = CliTypeIdentity.Named(
            assembly.Identity,
            "",
            "Implementation",
            false);

        var implementations = resolver.GetMethodImplementations(identity);
        var constructedImplementations = resolver.GetMethodImplementations(
            CliTypeIdentity.GenericInstantiation(identity, [
                CliTypeIdentity.Primitive("i4", CliValueKind.I4)]));

        Assert.NotEmpty(implementations);
        Assert.Equal(implementations.Length, constructedImplementations.Length);
    }

    private sealed class UnusedAssemblyResolver : IMetadataAssemblyResolver
    {
        public MetadataAssemblySnapshot Resolve(AssemblyIdentity identity) =>
            throw new InvalidOperationException("Assembly resolution must follow type resolution.");
    }

    private sealed class UnusedMethodReferenceResolver : IMetadataMethodReferenceResolver
    {
        public MethodInstanceModel Resolve(
            MetadataAssemblySnapshot source,
            int metadataToken,
            string methodDisplayName,
            int ilOffset,
            CliGenericContext? genericContext = null) =>
            throw new InvalidOperationException("Method resolution must follow type resolution.");
    }

    private sealed class FailingTypeDefinitionResolver : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            throw new InvalidOperationException("type-resolution");
    }

    private sealed class FixedAssemblyResolver(MetadataAssemblySnapshot source) :
        IMetadataAssemblyResolver
    {
        public MetadataAssemblySnapshot Resolve(AssemblyIdentity identity) => source;
    }

    private sealed class FixedMethodReferenceResolver(MethodInstanceModel result) :
        IMetadataMethodReferenceResolver
    {
        public MethodInstanceModel Resolve(
            MetadataAssemblySnapshot source,
            int metadataToken,
            string methodDisplayName,
            int ilOffset,
            CliGenericContext? genericContext = null) => result;
    }

    private sealed class FixedTypeDefinitionResolver(TypeDefinitionModel result) :
        ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => result;
    }
}
