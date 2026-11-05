using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class WasmBinaryImportSignatureReaderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("netwasm-imports-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void ReadsFunctionImportsInOrderWithTheirSignatures()
    {
        var types = Types(Func([0x7F, 0x7E], [0x7D]), Func([0x7C], []), Func([0x7B], []));
        var imports = Imports(Import("env", "a", 0), Import("host", "b", 1));
        var reader = new WasmBinaryImportSignatureReader(new FailingFallback());

        var result = reader.Read(Request(Module(Custom(), types, imports, Section(3, [0]))));

        Assert.Equal(
            [
                new RawCanonicalImportIdentity("env", "a"),
                new RawCanonicalImportIdentity("host", "b"),
            ],
            result.Select(import => import.Identity));
        Assert.Equal([RawCoreValueType.I32, RawCoreValueType.I64], result[0].Parameters);
        Assert.Equal([RawCoreValueType.F32], result[0].Results);
        Assert.Equal([RawCoreValueType.F64], result[1].Parameters);
        Assert.Empty(result[1].Results);
    }

    [Fact]
    public void KeepsTheFirstOfRepeatedImportsThatAgree()
    {
        var reader = new WasmBinaryImportSignatureReader(new FailingFallback());

        var result = reader.Read(Request(Module(
            Types(Func([0x7F], []), Func([0x7F], [])),
            Imports(Import("env", "a", 0), Import("env", "a", 1)))));

        Assert.Equal(new RawCanonicalImportIdentity("env", "a"), Assert.Single(result).Identity);
    }

    [Fact]
    public void ReadsNoImportsFromAModuleWithoutAnImportSection()
    {
        var reader = new WasmBinaryImportSignatureReader(new FailingFallback());

        Assert.Empty(reader.Read(Request(Module(Types(Func([], [])), Section(3, [0])))));
        Assert.Empty(reader.Read(Request(Module())));
    }

    [Theory]
    [InlineData("disagree", "inconsistent-import-inventory")]
    [InlineData("memory", "unsupported-import-kind")]
    [InlineData("v128", "unsupported-import-signature")]
    [InlineData("type-index", "invalid-core-module")]
    [InlineData("magic", "invalid-core-module")]
    [InlineData("truncated", "invalid-core-module")]
    [InlineData("name", "invalid-core-module")]
    [InlineData("leb", "invalid-core-module")]
    public void RejectsWhatTheInspectionCommandRejects(string shape, string code)
    {
        byte[] bytes = shape switch
        {
            "disagree" => Module(Types(Func([0x7F], []), Func([0x7E], [])),
                Imports(Import("env", "a", 0), Import("env", "a", 1))),
            "memory" => Module(Imports([.. Name("env"), .. Name("memory"), 0x02, 0x00, 0x01])),
            "v128" => Module(Types(Func([0x7B], [])), Imports(Import("env", "a", 0))),
            "type-index" => Module(Types(Func([], [])), Imports(Import("env", "a", 3))),
            "magic" => [0, (byte)'a', (byte)'s', (byte)'n', 1, 0, 0, 0],
            "truncated" => Module(Section(2, [1])),
            "name" => Module(Section(2, [1, 9, (byte)'e'])),
            "leb" => [.. Module(), 2, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        var reader = new WasmBinaryImportSignatureReader(new FailingFallback());

        var exception = Assert.Throws<CompilerException>(() => reader.Read(Request(bytes)));

        Assert.Equal(DiagnosticCode.ComponentToolchain, exception.Diagnostic.Code);
        Assert.Contains($"'{code}'", exception.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new byte[] { 0x4E, 0x00 })]
    [InlineData(new byte[] { 0x60, 0x01, 0x63, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x60, 0x00, 0x01, 0x64, 0x00 })]
    public void LeavesTypeFormsItDoesNotModelToTheFallback(byte[] type)
    {
        var fallback = new RecordingFallback();
        var reader = new WasmBinaryImportSignatureReader(fallback);
        var request = Request(Module(Section(1, [1, .. type]), Imports(Import("env", "a", 0))));

        Assert.Equal(RecordingFallback.Result, reader.Read(request));
        Assert.Same(request, fallback.Request);
    }

    [Fact]
    public void ReportsAnUnreadableModule()
    {
        var reader = new WasmBinaryImportSignatureReader(new FailingFallback());

        var exception = Assert.Throws<CompilerException>(() =>
            reader.Read(new("node", "script", Path.Combine(_directory, "missing.wasm"), "binaryen")));

        Assert.Contains("'invalid-bytes'", exception.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiresAFallback() =>
        Assert.Throws<ArgumentNullException>(() => new WasmBinaryImportSignatureReader(null!));

    private RawModuleInspectionRequest Request(byte[] module)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".wasm");
        File.WriteAllBytes(path, module);
        return new("node", "script", path, "binaryen");
    }

    private static byte[] Module(params byte[][] sections) =>
        [0, (byte)'a', (byte)'s', (byte)'m', 1, 0, 0, 0, .. sections.SelectMany(section => section)];

    private static byte[] Custom() => Section(0, [.. Name("name")]);

    private static byte[] Types(params byte[][] types) =>
        Section(1, [(byte)types.Length, .. types.SelectMany(type => type)]);

    private static byte[] Func(byte[] parameters, byte[] results) =>
        [0x60, (byte)parameters.Length, .. parameters, (byte)results.Length, .. results];

    private static byte[] Imports(params byte[][] imports) =>
        Section(2, [(byte)imports.Length, .. imports.SelectMany(import => import)]);

    private static byte[] Import(string module, string name, byte type) =>
        [.. Name(module), .. Name(name), 0x00, type];

    private static byte[] Name(string value) =>
        [(byte)value.Length, .. System.Text.Encoding.UTF8.GetBytes(value)];

    private static byte[] Section(byte id, byte[] content) => [id, (byte)content.Length, .. content];

    private sealed class FailingFallback : IRawModuleImportSignatureReader
    {
        public ImmutableArray<RawCoreFunctionImportSignature> Read(RawModuleInspectionRequest request) =>
            throw new InvalidOperationException("The fallback must not run.");
    }

    private sealed class RecordingFallback : IRawModuleImportSignatureReader
    {
        public static readonly ImmutableArray<RawCoreFunctionImportSignature> Result =
            [new(new("fallback", "import"), [], [])];

        public RawModuleInspectionRequest? Request { get; private set; }

        public ImmutableArray<RawCoreFunctionImportSignature> Read(RawModuleInspectionRequest request)
        {
            Request = request;
            return Result;
        }
    }
}
