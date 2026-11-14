using NetWasm.Compiler.Core;
using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataTypeFinderTests
{
    [Fact]
    public void NameLookupUsesCanonicalDefinitionsWithoutHidingUnrelatedAmbiguity()
    {
        var original = MetadataActorTestData.Type;
        var platform = new AssemblyIdentity("Platform");
        var target = original with { Key = original.Key with { Assembly = platform } };
        var packageOnly = original with { Name = "PackageOnly", Key = original.Key with { MetadataToken = 2 } };
        var source = new MetadataAssemblySnapshot(original.Key.Assembly, null!,
            new Dictionary<int, TypeDefinitionModel>(), new Dictionary<int, FieldDefinitionModel>(),
            new Dictionary<int, MethodDefinitionModel>(), new Dictionary<int, EntityHandle>())
        {
            AssemblyIdentityAliases = new AssemblyIdentityAliases(ImmutableDictionary<string, string>.Empty,
                ImmutableDictionary<(AssemblyIdentity, string), AssemblyIdentity>.Empty
                    .Add((original.Key.Assembly, original.FullName), platform))
        };
        var assemblies = new Dictionary<string, MetadataAssemblySnapshot> { [original.Key.Assembly.Name] = source };
        var finder = new MetadataTypeFinder([original, target, packageOnly],
            MetadataActorTestData.CreateAvailabilityValidator(), assemblies);

        Assert.Same(target, finder.FindType(original.FullName));
        Assert.Same(packageOnly, finder.FindType(packageOnly.FullName));
        var missing = new MetadataTypeFinder([original, packageOnly],
            MetadataActorTestData.CreateAvailabilityValidator(), assemblies);
        Assert.Throws<CompilerException>(() => missing.FindType(original.FullName));
        var unrelated = original with { Key = original.Key with { Assembly = new("Unrelated") } };
        var ambiguous = new MetadataTypeFinder([original, target, unrelated],
            MetadataActorTestData.CreateAvailabilityValidator(), assemblies);
        Assert.Throws<CompilerException>(() => ambiguous.FindType(original.FullName));
    }

    [Fact]
    public void FindTypeRequiresOneNamedMatch()
    {
        var finder = new MetadataTypeFinder(
            [MetadataActorTestData.Type],
            MetadataActorTestData.CreateAvailabilityValidator());

        Assert.Same(
            MetadataActorTestData.Type,
            ((ITypeFinder)finder).FindType("Test.Namespace.Sample"));
        Assert.Throws<ArgumentException>(() => ((ITypeFinder)finder).FindType(""));
        Assert.Throws<CompilerException>(() =>
            ((ITypeFinder)finder).FindType("Missing"));

        finder = new MetadataTypeFinder(
            [MetadataActorTestData.Type, MetadataActorTestData.Type],
            MetadataActorTestData.CreateAvailabilityValidator());
        Assert.Throws<CompilerException>(() =>
            ((ITypeFinder)finder).FindType("Test.Namespace.Sample"));
    }

    [Fact]
    public void FindTypeReusesItsImmutableIndexWithoutPerLookupAllocation()
    {
        var finder = new MetadataTypeFinder(
            [MetadataActorTestData.Type],
            MetadataActorTestData.CreateAvailabilityValidator());

        _ = ((ITypeFinder)finder).FindType("Test.Namespace.Sample");
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 256; index++)
        {
            Assert.Same(
                MetadataActorTestData.Type,
                ((ITypeFinder)finder).FindType("Test.Namespace.Sample"));
        }
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(0, allocatedBytes);
    }
}
