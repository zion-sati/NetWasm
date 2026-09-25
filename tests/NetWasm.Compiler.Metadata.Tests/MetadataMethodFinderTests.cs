using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataMethodFinderTests
{
    [Fact]
    public void FindMethodRequiresOneMethodOnTheNamedAssemblyType()
    {
        var assembly = new AssemblyIdentity("Test.Assembly");
        var typeKey = new EntityKey(assembly, 1);
        var first = Method(new(assembly, 2), typeKey, "Run");
        var second = Method(new(assembly, 3), typeKey, "Run");
        var repository = new MetadataMethodRepository(
            ImmutableDictionary<EntityKey, MethodDefinitionModel>.Empty
                .Add(first.Key, first)
                .Add(second.Key, second),
            MetadataActorTestData.CreateAvailabilityValidator());
        var unique = new MetadataMethodFinder(
            [Type(typeKey, [first.Key])],
            repository,
            MetadataActorTestData.CreateAvailabilityValidator());

        Assert.Same(
            first,
            ((IMethodFinder)unique).FindMethod(assembly, "Test.Sample", "Run"));
        Assert.Same(
            first,
            ((IMethodFinder)unique).FindMethod(assembly, first.Key.MetadataToken));
        Assert.Throws<CompilerException>(() =>
            ((IMethodFinder)unique).FindMethod(assembly, "Missing", "Run"));
        Assert.Throws<CompilerException>(() =>
            ((IMethodFinder)unique).FindMethod(assembly, "Test.Sample", "Missing"));

        var ambiguous = new MetadataMethodFinder(
            [Type(typeKey, [first.Key, second.Key])],
            repository,
            MetadataActorTestData.CreateAvailabilityValidator());
        Assert.Throws<CompilerException>(() =>
            ((IMethodFinder)ambiguous).FindMethod(assembly, "Test.Sample", "Run"));
        Assert.Throws<CompilerException>(() =>
            ((IMethodFinder)unique).FindMethod(assembly, 0x0600ffff));
    }

    private static TypeDefinitionModel Type(
        EntityKey key,
        ImmutableArray<EntityKey> methods) => new(
            key,
            "Test",
            "Sample",
            false,
            [],
            methods);

    private static MethodDefinitionModel Method(
        EntityKey key,
        EntityKey declaringType,
        string name) => new(
            key,
            declaringType,
            name,
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            0);
}
