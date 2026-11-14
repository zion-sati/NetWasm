using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

namespace NetWasm.Compiler.Metadata.Tests;

// The throwing body is built only for a P/Invoke whose library the program does not link; the
// method bodies of linked imports and of managed methods never come here.
public sealed class UnboundNativeImportBodyBuilderTests
{
    [Fact]
    public void OnlyAnUnboundNativeImportGetsAThrowingBody()
    {
        var builder = new UnboundNativeImportBodyBuilder(null!, null!, null!);
        var method = new MethodInstanceModel(
            MetadataActorTestData.Method,
            CliTypeIdentity.FromDefinition(MetadataActorTestData.Type),
            [],
            MetadataActorTestData.Method.Signature);

        Assert.Throws<ArgumentNullException>("method", () => builder.Build(null!));
        Assert.Throws<ArgumentException>("method", () => builder.Build(method));
    }
}
