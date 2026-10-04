using System.Text;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class WasmCoreModuleExportEditorTests
{
    [Fact]
    public void RejectsMissingFileCapabilitiesBeforeModuleAccess()
    {
        var files = new MemoryFiles(Module());

        Assert.Throws<ArgumentNullException>(() => new WasmCoreModuleExportEditor(null!, files, files));
        Assert.Throws<ArgumentNullException>(() => new WasmCoreModuleExportEditor(files, null!, files));
        Assert.Throws<ArgumentNullException>(() => new WasmCoreModuleExportEditor(files, files, null!));

        Assert.Equal(0, files.Reads);
        Assert.Null(files.Written);
    }

    [Fact]
    public void PreservesLongSelectedExportsAndOriginalNonExportLengthEncoding()
    {
        var payload = ExportPayload(Export("cm32p2_memory", 2, 0), Export("cm32p2|" + new string('x', 150), 0, 0));
        var canonical = ModuleWithSections((7, payload));
        byte[] original = [.. canonical.AsSpan(0, 8), 0, 0x81, 0, 0, .. canonical.AsSpan(8)];
        var files = new MemoryFiles(original);

        new WasmCoreModuleExportEditor(files, files, files).Rewrite("input", "output", new("cm32p2", []));

        Assert.Equal(original, files.Written);
    }

    [Fact]
    public void RetainsOnlyCanonicalComponentExports()
    {
        using var files = new ComponentModelTestFiles();
        var input = files.PathFor("input.wasm");
        var output = files.PathFor("output.wasm");
        File.WriteAllBytes(input, Module(
            Export("memory", 2, 0),
            Export("cm32p2_memory", 2, 0),
            Export("run", 0, 0)));

        new WasmCoreModuleExportEditor(
            new SystemFileExistence(), new SystemByteFileReader(), new SystemByteFileWriter())
            .Rewrite(
            input,
            output,
            new("cm32p2", []));

        Assert.Equal(
            Module(Export("cm32p2_memory", 2, 0)),
            File.ReadAllBytes(output));
    }

    [Fact]
    public void RejectsMissingOrDuplicateSelectedMemoryExports()
    {
        using var files = new ComponentModelTestFiles();
        var input = files.PathFor("input.wasm");
        var output = files.PathFor("output.wasm");
        var editor = new WasmCoreModuleExportEditor(
            new SystemFileExistence(), new SystemByteFileReader(), new SystemByteFileWriter());
        File.WriteAllBytes(input, Module(Export("other", 2, 0)));
        var missing = Assert.Throws<CompilerException>(() =>
            editor.Rewrite(input, output, new("cm32p2", [])));
        Assert.Contains("exactly one memory", missing.Diagnostic.Message);

        File.WriteAllBytes(input, Module(
            Export("cm32p2_memory", 2, 0),
            Export("cm32p2_memory", 2, 0)));
        var duplicate = Assert.Throws<CompilerException>(() =>
            editor.Rewrite(input, output, new("cm32p2", [])));
        Assert.Contains("exactly one memory", duplicate.Diagnostic.Message);
    }

    [Fact]
    public void RejectsMalformedModulesDeterministically()
    {
        using var files = new ComponentModelTestFiles();
        var input = files.PathFor("input.wasm");
        var output = files.PathFor("output.wasm");
        File.WriteAllBytes(input, [0x00, 0x61]);

        var exception = Assert.Throws<CompilerException>(() =>
            new WasmCoreModuleExportEditor(
                new SystemFileExistence(), new SystemByteFileReader(), new SystemByteFileWriter())
                .Rewrite(
                input,
                output,
                new("cm32p2", [])));

        Assert.Equal(DiagnosticCode.ComponentContract, exception.Diagnostic.Code);
        Assert.Equal("core module is truncated", exception.Diagnostic.Message);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void PreservesNonExportSectionsAndWritesMultiByteSectionLengths()
    {
        using var files = new ComponentModelTestFiles();
        var input = files.PathFor("input.wasm");
        var output = files.PathFor("output.wasm");
        var custom = Enumerable.Repeat((byte)0x2a, 128).ToArray();
        File.WriteAllBytes(input, ModuleWithSections(
            (1, custom),
            (7, ExportPayload(Export("cm32p2_memory", 2, 0)))));

        new WasmCoreModuleExportEditor(
            new SystemFileExistence(), new SystemByteFileReader(), new SystemByteFileWriter())
            .Rewrite(
            input, output, new("cm32p2", []));

        Assert.Equal(ModuleWithSections(
            (1, custom),
            (7, ExportPayload(Export("cm32p2_memory", 2, 0)))),
            File.ReadAllBytes(output));
    }

    [Fact]
    public void RejectsInvalidHeadersLebAndExportEncoding()
    {
        using var files = new ComponentModelTestFiles();
        var input = files.PathFor("input.wasm");
        var output = files.PathFor("output.wasm");
        var editor = new WasmCoreModuleExportEditor(
            new SystemFileExistence(), new SystemByteFileReader(), new SystemByteFileWriter());

        File.WriteAllBytes(input, [0, 1, 2, 3, 4, 5, 6, 7]);
        var header = Assert.Throws<CompilerException>(() =>
            editor.Rewrite(input, output, new("cm32p2", [])));
        Assert.Contains("invalid WebAssembly header", header.Diagnostic.Message);

        File.WriteAllBytes(input, [
            0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00,
            0x01, 0x80, 0x80, 0x80, 0x80, 0x80,
        ]);
        var leb = Assert.Throws<CompilerException>(() =>
            editor.Rewrite(input, output, new("cm32p2", [])));
        Assert.Contains("invalid unsigned LEB128", leb.Diagnostic.Message);

        File.WriteAllBytes(input, [
            0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00,
            0x01, 0xff, 0xff, 0xff, 0xff, 0x0f,
        ]);
        var oversized = Assert.Throws<CompilerException>(() =>
            editor.Rewrite(input, output, new("cm32p2", [])));
        Assert.Contains("truncated", oversized.Diagnostic.Message);

        File.WriteAllBytes(input, ModuleWithSections((7, [
            0x01, 0x01, 0xff, 0x02, 0x00,
        ])));
        var utf8 = Assert.Throws<CompilerException>(() =>
            editor.Rewrite(input, output, new("cm32p2", [])));
        Assert.Contains("not valid UTF-8", utf8.Diagnostic.Message);
    }

    [Fact]
    public void RejectsTruncatedAndTrailingExportSections()
    {
        using var files = new ComponentModelTestFiles();
        var input = files.PathFor("input.wasm");
        var output = files.PathFor("output.wasm");
        var editor = new WasmCoreModuleExportEditor(
            new SystemFileExistence(), new SystemByteFileReader(), new SystemByteFileWriter());

        File.WriteAllBytes(input, [
            0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00,
            0x07, 0x01, 0x80,
        ]);
        var truncated = Assert.Throws<CompilerException>(() =>
            editor.Rewrite(input, output, new("cm32p2", [])));
        Assert.Contains("truncated", truncated.Diagnostic.Message);

        File.WriteAllBytes(input, ModuleWithSections((7, [0x00, 0xff])));
        var trailing = Assert.Throws<CompilerException>(() =>
            editor.Rewrite(input, output, new("cm32p2", [])));
        Assert.Contains("trailing data", trailing.Diagnostic.Message);
    }

    [Fact]
    public void RejectsMissingFilesAndBlankBoundaryArguments()
    {
        using var files = new ComponentModelTestFiles();
        var editor = new WasmCoreModuleExportEditor(
            new SystemFileExistence(), new SystemByteFileReader(), new SystemByteFileWriter());
        var input = files.PathFor("input.wasm");
        File.WriteAllBytes(input, Module(Export("cm32p2_memory", 2, 0)));

        Assert.Throws<CompilerException>(() => editor.Rewrite(
            files.PathFor("missing.wasm"), files.PathFor("output.wasm"), new("cm32p2", [])));
        Assert.Throws<ArgumentException>(() => editor.Rewrite(
            input, " ", new("cm32p2", [])));
        Assert.Throws<ArgumentException>(() => editor.Rewrite(
            input, files.PathFor("output.wasm"), new(" ", [])));
    }

    [Fact]
    public void RemovesExactInternalNamesAndPreservesAllOtherEntriesAndSections()
    {
        var original = ModuleWithSections((0, [1, 2, 3]), (7, ExportPayload(
            Export("memory", 2, 0), Export("__indirect_function_table", 1, 0), Export("public", 0, 0),
            Export("native", 0, 1), Export("nativeSimilar", 0, 2), Export("__heap_base", 3, 0))));
        var files = new MemoryFiles(original);
        var editor = Assert.IsAssignableFrom<IWasmCoreModuleExportEditor>(new WasmCoreModuleExportEditor(files, files, files));
        editor.Rewrite("input", "output", new(null, [new("native", 0), new("__heap_base", 3)]));
        Assert.Equal(ModuleWithSections((0, [1, 2, 3]), (7, ExportPayload(
            Export("memory", 2, 0), Export("__indirect_function_table", 1, 0), Export("public", 0, 0),
            Export("nativeSimilar", 0, 2)))), files.Written);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingWrongKindOrRepeatedInternalExportsFailWithoutPublication(bool duplicate)
    {
        var files = new MemoryFiles(duplicate ? Module(Export("native", 0, 0), Export("native", 0, 1)) : Module(Export("native", 3, 0)));
        var editor = new WasmCoreModuleExportEditor(files, files, files);
        Assert.Throws<CompilerException>(() => editor.Rewrite("input", "output", new(null, [new("native", 0)])));
        Assert.Null(files.Written);
        Assert.Throws<CompilerException>(() => editor.Rewrite("input", "output", new(null, [new("missing", 0)])));
        Assert.Null(files.Written);
    }

    [Fact]
    public void EmptyExactRemovalDoesNotRequireComponentMemory()
    {
        var original = Module(Export("public", 0, 0));
        var files = new MemoryFiles(original);
        new WasmCoreModuleExportEditor(files, files, files).Rewrite("input", "output", new(null, []));
        Assert.Equal(original, files.Written);
    }

    [Fact]
    public void RejectsInvalidPolicyBeforeReadingOrWriting()
    {
        var files = new MemoryFiles(Module());
        var editor = new WasmCoreModuleExportEditor(files, files, files);
        Assert.Throws<ArgumentNullException>(() => editor.Rewrite("input", "output", null!));
        foreach (var policy in new WasmExportSelection[]
        {
            new(null, default), new("cm32p2", [new("native", 0)]), new(null, [null!]),
            new(null, [new(" ", 0)]), new(null, [new("n", 5)]), new(null, [new("n", 0), new("n", 0)]),
        }) Assert.Throws<CompilerException>(() => editor.Rewrite("input", "output", policy));
        Assert.Equal(0, files.Reads);
        Assert.Null(files.Written);
    }

    private sealed class MemoryFiles(byte[] module) : IFileExistence, IByteFileReader, IByteFileWriter
    {
        public int Reads { get; private set; }
        public byte[]? Written { get; private set; }
        public bool Exists(string path) => true;
        public byte[] Read(string path) { Reads++; return module; }
        public void Write(string path, byte[] content) => Written = content;
    }

    private static byte[] Module(params byte[][] exports)
    {
        var payload = new List<byte> { (byte)exports.Length };
        foreach (var export in exports)
        {
            payload.AddRange(export);
        }
        return
        [
            0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00,
            0x07, (byte)payload.Count, .. payload,
        ];
    }

    private static byte[] ModuleWithSections(params (byte Id, byte[] Payload)[] sections)
    {
        var bytes = new List<byte>
        {
            0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00,
        };
        foreach (var section in sections)
        {
            bytes.Add(section.Id);
            WriteUnsigned(bytes, (uint)section.Payload.Length);
            bytes.AddRange(section.Payload);
        }
        return [.. bytes];
    }

    private static void WriteUnsigned(List<byte> bytes, uint value)
    {
        do
        {
            var next = (byte)(value & 0x7f);
            value >>= 7;
            if (value != 0)
            {
                next |= 0x80;
            }
            bytes.Add(next);
        }
        while (value != 0);
    }

    private static byte[] Export(string name, byte kind, byte index)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        var encoded = new List<byte>();
        WriteUnsigned(encoded, (uint)bytes.Length);
        return [.. encoded, .. bytes, kind, index];
    }

    private static byte[] ExportPayload(params byte[][] exports) =>
        [(byte)exports.Length, .. exports.SelectMany(export => export)];

}
