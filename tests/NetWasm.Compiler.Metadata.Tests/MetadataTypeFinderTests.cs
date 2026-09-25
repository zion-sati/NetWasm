using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataTypeFinderTests
{
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
