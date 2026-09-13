using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NetWasm.Compiler.ComponentModel;

public interface IWasmCoreModuleExportEditor
{
    void RetainComponentExports(string inputPath, string outputPath, string prefix);
}

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

    public void RetainComponentExports(string inputPath, string outputPath, string prefix)
    {
        RequireFile(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        var module = _reader.Read(inputPath);
        var reader = new BinaryReader(module);
        reader.RequireHeader();
        using var output = new MemoryStream(module.Length);
        output.Write(WasmBinaryFormat.Header);
        var memoryExports = 0;
        while (!reader.IsAtEnd)
        {
            var sectionId = reader.ReadByte();
            var sectionSize = reader.ReadUnsigned();
            var section = reader.ReadBytes(sectionSize);
            if (sectionId != WasmBinaryFormat.ExportSection)
            {
                output.WriteByte(sectionId);
                WriteUnsigned(output, sectionSize);
                output.Write(section);
                continue;
            }

            var rewritten = RewriteExportSection(section, prefix, ref memoryExports);
            output.WriteByte(sectionId);
            WriteUnsigned(output, (uint)rewritten.Length);
            output.Write(rewritten);
        }

        if (memoryExports != 1)
        {
            throw ComponentException.Invalid(
                $"core module must export exactly one memory named '{prefix}_memory'");
        }
        _writer.Write(outputPath, output.ToArray());
    }

    private static byte[] RewriteExportSection(
        ReadOnlySpan<byte> section,
        string prefix,
        ref int memoryExports)
    {
        var reader = new BinaryReader(section);
        var count = reader.ReadUnsigned();
        var retained = new List<byte[]>((int)count);
        for (var index = 0u; index < count; index++)
        {
            var start = reader.Offset;
            var name = reader.ReadString();
            var kind = reader.ReadByte();
            reader.ReadUnsigned();
            var entry = section[start..reader.Offset].ToArray();
            if (string.Equals(name, prefix + "_memory", StringComparison.Ordinal) &&
                kind == WasmBinaryFormat.MemoryExternalKind)
            {
                memoryExports++;
            }
            if (IsComponentExport(name, prefix))
            {
                retained.Add(entry);
            }
        }
        reader.RequireEnd("export section");

        using var output = new MemoryStream(section.Length);
        WriteUnsigned(output, (uint)retained.Count);
        foreach (var entry in retained)
        {
            output.Write(entry);
        }
        return output.ToArray();
    }

    private static bool IsComponentExport(string name, string prefix) =>
        name.StartsWith(prefix + "_", StringComparison.Ordinal) ||
        name.StartsWith(prefix + "|", StringComparison.Ordinal);

    private void RequireFile(string path)
    {
        if (!_files.Exists(path))
        {
            throw ComponentException.Invalid($"core module '{path}' does not exist");
        }
    }

    private static void WriteUnsigned(Stream output, uint value)
    {
        do
        {
            var next = (byte)(value & 0x7f);
            value >>= 7;
            if (value != 0)
            {
                next |= 0x80;
            }
            output.WriteByte(next);
        }
        while (value != 0);
    }

    private ref struct BinaryReader
    {
        private readonly ReadOnlySpan<byte> _bytes;

        public BinaryReader(ReadOnlySpan<byte> bytes)
        {
            _bytes = bytes;
        }

        public int Offset { get; private set; }
        public bool IsAtEnd => Offset == _bytes.Length;

        public void RequireHeader()
        {
            var header = ReadBytes((uint)WasmBinaryFormat.Header.Length);
            if (!header.SequenceEqual(WasmBinaryFormat.Header))
            {
                throw Invalid("core module has an invalid WebAssembly header");
            }
        }

        public byte ReadByte()
        {
            if (Offset >= _bytes.Length)
            {
                throw Invalid("core module is truncated");
            }
            return _bytes[Offset++];
        }

        public uint ReadUnsigned()
        {
            var value = 0u;
            for (var shift = 0; shift < 35; shift += 7)
            {
                var next = ReadByte();
                value |= (uint)(next & 0x7f) << shift;
                if ((next & 0x80) == 0)
                {
                    return value;
                }
            }
            throw Invalid("core module contains an invalid unsigned LEB128 value");
        }

        public ReadOnlySpan<byte> ReadBytes(uint count)
        {
            if (count > int.MaxValue || Offset > _bytes.Length - (int)count)
            {
                throw Invalid("core module is truncated");
            }
            var result = _bytes.Slice(Offset, (int)count);
            Offset += (int)count;
            return result;
        }

        public string ReadString()
        {
            var bytes = ReadBytes(ReadUnsigned());
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                throw Invalid("core module export name is not valid UTF-8");
            }
        }

        public void RequireEnd(string description)
        {
            if (!IsAtEnd)
            {
                throw Invalid($"core module {description} contains trailing data");
            }
        }

        private static Compiler.Core.CompilerException Invalid(string message) =>
            ComponentException.Invalid(message);
    }
}
