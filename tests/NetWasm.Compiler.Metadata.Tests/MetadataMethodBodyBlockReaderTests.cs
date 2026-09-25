using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataMethodBodyBlockReaderTests
{
    [Fact]
    public void ReadReportsMethodsWithoutBodiesThroughItsInterface()
    {
        var reader = new MetadataMethodBodyBlockReader(new FixedSymbols());
        var source = (PEReader)RuntimeHelpers.GetUninitializedObject(typeof(PEReader));

        var exception = Assert.Throws<CompilerException>(() =>
            ((IMetadataMethodBodyBlockReader)reader)
                .Read(source, MetadataActorTestData.Method));

        Assert.Equal(DiagnosticCode.InvalidCil, exception.Diagnostic.Code);
        Assert.Equal("Test.Namespace.Sample::Run", exception.Diagnostic.Method);
        Assert.Contains("method has no CIL body", exception.Message);
    }

    private sealed class FixedSymbols : ISymbolFormatter
    {
        public string Format(EntityKey key) => "Test.Namespace.Sample";

        public string Format(MethodDefinitionModel method) =>
            "Test.Namespace.Sample::Run";
    }
}
