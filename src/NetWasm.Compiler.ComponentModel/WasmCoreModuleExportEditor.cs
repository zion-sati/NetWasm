using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using NetWasm.Compiler.ComponentModel.Binary;

namespace NetWasm.Compiler.ComponentModel;

public interface IWasmCoreModuleExportEditor
{
    void Rewrite(string inputPath, string outputPath, WasmExportSelection selection);
}

public sealed record WasmInternalExport(string Name, byte Kind);

public sealed record WasmExportSelection(string? ComponentPrefix, ImmutableArray<WasmInternalExport> RemovedExports);

public sealed class WasmCoreModuleExportEditor(
    IFileExistence files,
    IByteFileReader reader,
    IByteFileWriter writer) : IWasmCoreModuleExportEditor
{
    private readonly IFileExistence _files = files ??
        throw new ArgumentNullException(nameof(files));
    private readonly IByteFileReader _reader = reader ??
        throw new ArgumentNullException(nameof(reader));
    private readonly IByteFileWriter _writer = writer ??
        throw new ArgumentNullException(nameof(writer));

    public void Rewrite(string inputPath, string outputPath, WasmExportSelection selection)
    {
        var filter = new WasmExportFilter(selection);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        RequireFile(inputPath);

        var module = _reader.Read(inputPath);
        var reader = new WasmBinaryReader(module);
        reader.RequireHeader();
        using var output = new MemoryStream(module.Length);
        output.Write(WasmBinaryFormat.Header);
        while (!reader.IsAtEnd)
        {
            var sectionStart = reader.Offset;
            var sectionId = reader.ReadByte();
            var sectionSize = reader.ReadUnsigned();
            var section = reader.ReadBytes(sectionSize);
            if (sectionId != WasmBinaryFormat.ExportSection)
            {
                output.Write(module.AsSpan(sectionStart, reader.Offset - sectionStart));
                continue;
            }

            var rewritten = RewriteExportSection(section, filter);
            output.WriteByte(sectionId);
            WasmUnsignedLeb128.Write(output, (uint)rewritten.Length);
            output.Write(rewritten);
        }

        filter.Complete();
        _writer.Write(outputPath, output.ToArray());
    }

    private static byte[] RewriteExportSection(ReadOnlySpan<byte> section, WasmExportFilter filter)
    {
        var reader = new WasmBinaryReader(section);
        var count = reader.ReadUnsigned();
        var retained = new List<byte[]>((int)count);
        for (var index = 0u; index < count; index++)
        {
            var start = reader.Offset;
            var name = reader.ReadString();
            var kind = reader.ReadByte();
            reader.ReadUnsigned();
            if (filter.Keeps(name, kind))
            {
                retained.Add(section[start..reader.Offset].ToArray());
            }
        }
        reader.RequireEnd("export section");

        using var output = new MemoryStream(section.Length);
        WasmUnsignedLeb128.Write(output, (uint)retained.Count);
        foreach (var entry in retained)
        {
            output.Write(entry);
        }
        return output.ToArray();
    }

    private void RequireFile(string path)
    {
        if (!_files.Exists(path))
        {
            throw ComponentException.Invalid($"core module '{path}' does not exist");
        }
    }

}
