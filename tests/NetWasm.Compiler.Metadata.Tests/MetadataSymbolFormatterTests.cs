using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataSymbolFormatterTests
{
    [Fact]
    public void FormatProducesTypeAndMethodSymbols()
    {
        var formatter = new MetadataSymbolFormatter(
            new MetadataTypeRepository(MetadataActorTestData.One(
                MetadataActorTestData.TypeKey,
                MetadataActorTestData.Type),
                MetadataActorTestData.CreateAvailabilityValidator()));

        Assert.Equal(
            "Test.Namespace.Sample",
            ((ISymbolFormatter)formatter).Format(MetadataActorTestData.TypeKey));
        Assert.Equal(
            "Test.Namespace.Sample::Run",
            ((ISymbolFormatter)formatter).Format(MetadataActorTestData.Method));
    }
}
