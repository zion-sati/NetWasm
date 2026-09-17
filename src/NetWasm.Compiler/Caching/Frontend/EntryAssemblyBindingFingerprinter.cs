using NetWasm.Compiler.Analysis;
using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Compiler.Caching.Frontend;

internal interface IEntryAssemblyBindingFingerprinter
{
    string Fingerprint(ImmutableArray<byte> image);
}

internal sealed class EntryAssemblyBindingFingerprinter : IEntryAssemblyBindingFingerprinter
{
    private const string UserStringStream = "#US";

    public string Fingerprint(ImmutableArray<byte> image)
    {
        if (image.IsDefaultOrEmpty)
            throw new ArgumentException("An assembly image is required.", nameof(image));
        using var stream = new MemoryStream(image.AsSpan().ToArray(), writable: false);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var bytes = pe.GetMetadata().GetContent().ToArray();
        var mvid = metadata.GetModuleDefinition().Mvid;
        if (!mvid.IsNil)
        {
            var offset = metadata.GetHeapMetadataOffset(HeapIndex.Guid) +
                MetadataTokens.GetHeapOffset(mvid) - 1;
            bytes.AsSpan(offset, 16).Clear();
        }
        var tableOffset = metadata.GetTableMetadataOffset(TableIndex.MethodDef);
        var rowSize = metadata.GetTableRowSize(TableIndex.MethodDef);
        for (var row = 0; row < metadata.MethodDefinitions.Count; row++)
        {
            var rva = bytes.AsSpan(tableOffset + row * rowSize, sizeof(int));
            BinaryPrimitives.WriteUInt32LittleEndian(rva,
                BinaryPrimitives.ReadUInt32LittleEndian(rva) == 0 ? 0u : 1u);
        }
        var headers = pe.PEHeaders;
        var corHeader = headers.CorHeader!;
        var contract = string.Join('|', (int)headers.CoffHeader.Machine,
            (int)corHeader.Flags,
            corHeader.EntryPointTokenOrRelativeVirtualAddress);
        return FrontendArtifactCacheContext.Hash(contract + "|" +
            Convert.ToHexStringLower(SHA256.HashData(CanonicalizeMetadata(bytes))));
    }

    private static byte[] CanonicalizeMetadata(byte[] metadata)
    {
        var reader = new MetadataRootReader(metadata);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(reader.Signature);
        writer.Write(reader.MajorVersion);
        writer.Write(reader.MinorVersion);
        writer.Write(reader.Reserved);
        writer.Write(reader.Version.Length);
        writer.Write(reader.Version);
        writer.Write(reader.Flags);
        var streams = reader.Streams
            .Where(static item => item.Name != UserStringStream)
            .OrderBy(static item => item.Name, StringComparer.Ordinal)
            .ToArray();
        writer.Write(streams.Length);
        foreach (var item in streams)
        {
            writer.Write(item.Name);
            writer.Write(item.Content.Length);
            writer.Write(item.Content);
        }
        writer.Flush();
        return stream.ToArray();
    }

}

internal sealed class MetadataRootReader
{
    private readonly byte[] _metadata;
    private int _position;

    internal MetadataRootReader(byte[] metadata)
    {
        _metadata = metadata;
        Signature = ReadUInt32();
        MajorVersion = ReadUInt16();
        MinorVersion = ReadUInt16();
        Reserved = ReadUInt32();
        var versionLength = ReadSize();
        Version = ReadBytes(versionLength);
        Align();
        Flags = ReadUInt16();
        var streamCount = ReadUInt16();
        var streams = ImmutableArray.CreateBuilder<MetadataStream>(streamCount);
        for (var index = 0; index < streamCount; index++)
        {
            var offset = ReadSize();
            var size = ReadSize();
            var name = ReadStreamName();
            streams.Add(new(name, ReadBytesAt(offset, size)));
        }
        Streams = streams.MoveToImmutable();
    }

    internal uint Signature { get; }
    internal ushort MajorVersion { get; }
    internal ushort MinorVersion { get; }
    internal uint Reserved { get; }
    internal byte[] Version { get; }
    internal ushort Flags { get; }
    internal ImmutableArray<MetadataStream> Streams { get; }

    private ushort ReadUInt16() =>
        BinaryPrimitives.ReadUInt16LittleEndian(ReadSpan(sizeof(ushort)));

    private uint ReadUInt32() =>
        BinaryPrimitives.ReadUInt32LittleEndian(ReadSpan(sizeof(uint)));

    private int ReadSize()
    {
        var value = ReadUInt32();
        if (value > int.MaxValue)
            throw new BadImageFormatException("A metadata size exceeds the supported range.");
        return (int)value;
    }

    private byte[] ReadBytes(int length) => ReadSpan(length).ToArray();

    private byte[] ReadBytesAt(int offset, int length)
    {
        if (offset > _metadata.Length - length)
            throw new BadImageFormatException("A metadata stream extends beyond the metadata root.");
        return _metadata.AsSpan(offset, length).ToArray();
    }

    private string ReadStreamName()
    {
        var start = _position;
        while (_position < _metadata.Length && _metadata[_position] != 0)
            _position++;
        if (_position == _metadata.Length)
            throw new BadImageFormatException("A metadata stream name is unterminated.");
        var name = Encoding.ASCII.GetString(_metadata, start, _position - start);
        _position++;
        Align();
        return name;
    }

    private ReadOnlySpan<byte> ReadSpan(int length)
    {
        if (_position > _metadata.Length - length)
            throw new BadImageFormatException("The metadata root is truncated.");
        var result = _metadata.AsSpan(_position, length);
        _position += length;
        return result;
    }

    private void Align()
    {
        var aligned = checked((_position + 3) & ~3);
        if (aligned > _metadata.Length)
            throw new BadImageFormatException("The metadata root is truncated.");
        _position = aligned;
    }
}

internal sealed record MetadataStream(string Name, byte[] Content);
