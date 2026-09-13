using System.Text;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class WasmCoreModuleExportEditorTests
{
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
            .RetainComponentExports(
            input,
            output,
            "cm32p2");

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
            editor.RetainComponentExports(input, output, "cm32p2"));
        Assert.Contains("exactly one memory", missing.Diagnostic.Message);

        File.WriteAllBytes(input, Module(
            Export("cm32p2_memory", 2, 0),
            Export("cm32p2_memory", 2, 0)));
        var duplicate = Assert.Throws<CompilerException>(() =>
            editor.RetainComponentExports(input, output, "cm32p2"));
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
                .RetainComponentExports(
                input,
                output,
                "cm32p2"));

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
            .RetainComponentExports(
            input, output, "cm32p2");

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
            editor.RetainComponentExports(input, output, "cm32p2"));
        Assert.Contains("invalid WebAssembly header", header.Diagnostic.Message);

        File.WriteAllBytes(input, [
            0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00,
            0x01, 0x80, 0x80, 0x80, 0x80, 0x80,
        ]);
        var leb = Assert.Throws<CompilerException>(() =>
            editor.RetainComponentExports(input, output, "cm32p2"));
        Assert.Contains("invalid unsigned LEB128", leb.Diagnostic.Message);

        File.WriteAllBytes(input, [
            0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00,
            0x01, 0xff, 0xff, 0xff, 0xff, 0x0f,
        ]);
        var oversized = Assert.Throws<CompilerException>(() =>
            editor.RetainComponentExports(input, output, "cm32p2"));
        Assert.Contains("truncated", oversized.Diagnostic.Message);

        File.WriteAllBytes(input, ModuleWithSections((7, [
            0x01, 0x01, 0xff, 0x02, 0x00,
        ])));
        var utf8 = Assert.Throws<CompilerException>(() =>
            editor.RetainComponentExports(input, output, "cm32p2"));
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
            editor.RetainComponentExports(input, output, "cm32p2"));
        Assert.Contains("truncated", truncated.Diagnostic.Message);

        File.WriteAllBytes(input, ModuleWithSections((7, [0x00, 0xff])));
        var trailing = Assert.Throws<CompilerException>(() =>
            editor.RetainComponentExports(input, output, "cm32p2"));
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

        Assert.Throws<CompilerException>(() => editor.RetainComponentExports(
            files.PathFor("missing.wasm"), files.PathFor("output.wasm"), "cm32p2"));
        Assert.Throws<ArgumentException>(() => editor.RetainComponentExports(
            input, " ", "cm32p2"));
        Assert.Throws<ArgumentException>(() => editor.RetainComponentExports(
            input, files.PathFor("output.wasm"), " "));
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
        return [(byte)bytes.Length, .. bytes, kind, index];
    }

    private static byte[] ExportPayload(params byte[][] exports) =>
        [(byte)exports.Length, .. exports.SelectMany(export => export)];

}
