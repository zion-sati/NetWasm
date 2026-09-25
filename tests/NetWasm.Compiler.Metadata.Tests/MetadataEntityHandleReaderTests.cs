using System.Reflection.Metadata;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataEntityHandleReaderTests
{
    [Fact]
    public void ReadReturnsValidHandleAndReportsMalformedTokenAtItsMethodOffset()
    {
        var reader = new MetadataEntityHandleReader();

        var handle = ((IMetadataEntityHandleReader)reader).Read(
            0x02000001,
            "type",
            "Test::Run",
            12);
        Assert.Equal(HandleKind.TypeDefinition, handle.Kind);

        var exception = Assert.Throws<CompilerException>(() =>
            ((IMetadataEntityHandleReader)reader).Read(
                -1,
                "type",
                "Test::Run",
                12));
        Assert.Equal(DiagnosticCode.InvalidCil, exception.Diagnostic.Code);
        Assert.Equal("Test::Run", exception.Diagnostic.Method);
        Assert.Equal(12, exception.Diagnostic.IlOffset);
    }
}
