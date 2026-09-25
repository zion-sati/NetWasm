using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataMethodRepositoryTests
{
    [Fact]
    public void GetMethodReturnsKnownMethodAndRejectsMissingKey()
    {
        var repository = new MetadataMethodRepository(
            MetadataActorTestData.One(
                MetadataActorTestData.MethodKey,
                MetadataActorTestData.Method),
            MetadataActorTestData.CreateAvailabilityValidator());

        Assert.Same(
            MetadataActorTestData.Method,
            ((IMethodRepository)repository).GetMethod(MetadataActorTestData.MethodKey));
        var exception = Assert.Throws<CompilerException>(() =>
            ((IMethodRepository)repository).GetMethod(new(new("Missing"), 3)));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
    }
}
