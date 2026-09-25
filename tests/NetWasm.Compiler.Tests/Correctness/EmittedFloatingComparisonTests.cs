using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class EmittedFloatingComparisonTests(CorrectnessTestRunner runner) : EmittedAssemblyTestBase(runner)
{
    public static TheoryData<string, string> MatrixCells => CorpusCaseTestData.Cells(CorpusCaseTestData.EmittedFloatingComparisons);

    [Theory]
    [MemberData(nameof(MatrixCells))]
    public void OrderedAndUnorderedInstructionsRespectNaNAndNumericOrdering(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell, new FloatingComparisonFixtureBuilder());

    [Fact]
    public void PersistedMethodsContainEveryIntendedComparisonAndBranchEncoding()
    {
        var builder = Assert.IsAssignableFrom<IEmittedAssemblyBuilder>(new FloatingComparisonFixtureBuilder());
        using var stream = new MemoryStream(builder.Build().ToArray());
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var methods = metadata.MethodDefinitions.Select(metadata.GetMethodDefinition)
            .ToDictionary(method => metadata.GetString(method.Name));
        // Independent ECMA-335 opcode encodings, not the builder's opcode table.
        (string Name, ushort Encoding)[] instructions =
        [
            ("ceq", 0xfe01), ("cgt", 0xfe02), ("cgt.un", 0xfe03),
            ("clt", 0xfe04), ("clt.un", 0xfe05),
            ("beq", 0x3b), ("bne.un", 0x40), ("bgt", 0x3d), ("bgt.un", 0x42),
            ("bge", 0x3c), ("bge.un", 0x41), ("blt", 0x3f), ("blt.un", 0x44),
            ("ble", 0x3e), ("ble.un", 0x43),
            ("beq.s", 0x2e), ("bne.un.s", 0x33), ("bgt.s", 0x30), ("bgt.un.s", 0x35),
            ("bge.s", 0x2f), ("bge.un.s", 0x34), ("blt.s", 0x32), ("blt.un.s", 0x37),
            ("ble.s", 0x31), ("ble.un.s", 0x36),
        ];
        Assert.Equal(50, methods.Keys.Count(name => name.StartsWith("Single_", StringComparison.Ordinal) ||
            name.StartsWith("Double_", StringComparison.Ordinal)));
        foreach (var single in new[] { true, false })
        {
            foreach (var (name, encoding) in instructions)
            {
                var method = methods[(single ? "Single_" : "Double_") + name];
                var parameter = single ? (byte)0x0c : (byte)0x0d;
                Assert.Equal<byte>([0x00, 0x02, 0x08, parameter, parameter],
                    metadata.GetBlobBytes(method.Signature));
                var body = pe.GetMethodBody(method.RelativeVirtualAddress);
                Assert.True(body.LocalSignature.IsNil);
                Assert.Empty(body.ExceptionRegions);
                byte[] expected = encoding > byte.MaxValue
                    ? [0x02, 0x03, 0xfe, (byte)encoding, 0x2a]
                    : name.EndsWith(".s", StringComparison.Ordinal)
                        ? [0x02, 0x03, (byte)encoding, 0x02, 0x16, 0x2a, 0x17, 0x2a]
                        : [0x02, 0x03, (byte)encoding, 0x02, 0x00, 0x00, 0x00, 0x16, 0x2a, 0x17, 0x2a];
                Assert.Equal(expected, body.GetILBytes());
            }
        }
    }
}
