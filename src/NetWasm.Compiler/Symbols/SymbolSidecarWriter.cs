using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Symbols;

public interface ISymbolSidecarWriter
{
    SymbolSidecarArtifact Write(SymbolSidecarKind kind, string buildId, ImmutableArray<SymbolSidecarEntry> entries);
}

// The sidecar format, all little-endian: u32 magic "NWSY", u16 version 1, u16 kind, the build ID as 32 UTF-16 hex
// digits, u32 entry count, u32 strings per entry (4 for types, 1 for methods), then per entry in ID order a u32 ID
// and per string a u32 character offset and an i32 character length (-1 for none), then the UTF-16 characters,
// where identical strings share one offset. UTF-16 is what a .NET string holds, so the program reads names without
// converting them, and repeated text compresses well.
public sealed class SymbolSidecarWriter : ISymbolSidecarWriter
{
    public const uint Magic = 0x5953574E;
    public const ushort Version = 1;
    public const int BuildIdLength = 32;
    public const int HeaderSize = 4 + 2 + 2 + BuildIdLength * sizeof(char) + 4 + 4;

    public SymbolSidecarArtifact Write(SymbolSidecarKind kind, string buildId, ImmutableArray<SymbolSidecarEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(buildId);
        if (buildId.Length != BuildIdLength)
            throw Invariant($"a symbols sidecar's build ID has {BuildIdLength} characters, not {buildId.Length}");
        if (entries.IsDefault)
            throw new ArgumentException("Symbols sidecar entries must be initialized.", nameof(entries));
        var stringsPerEntry = kind switch
        {
            SymbolSidecarKind.Type => 4,
            SymbolSidecarKind.Method => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var ordered = entries.OrderBy(entry => entry.Id).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            if (ordered[index].Strings.Length != stringsPerEntry)
                throw Invariant($"symbols sidecar entry {ordered[index].Id} has {ordered[index].Strings.Length} strings");
            if (index > 0 && ordered[index].Id == ordered[index - 1].Id)
                throw Invariant($"symbols sidecar entry {ordered[index].Id} appears twice");
        }

        var characters = new StringBuilder();
        var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
        var entrySize = sizeof(int) + stringsPerEntry * sizeof(int) * 2;
        var bytes = new byte[HeaderSize + ordered.Length * entrySize];
        var span = bytes.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], Version);
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], (ushort)kind);
        Encoding.Unicode.GetBytes(buildId, span[8..]);
        var cursor = 8 + BuildIdLength * sizeof(char);
        BinaryPrimitives.WriteInt32LittleEndian(span[cursor..], ordered.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span[(cursor + 4)..], stringsPerEntry);
        cursor = HeaderSize;
        foreach (var entry in ordered)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span[cursor..], entry.Id);
            cursor += sizeof(int);
            foreach (var value in entry.Strings)
            {
                var offset = 0;
                if (value is not null && !offsets.TryGetValue(value, out offset))
                {
                    offset = characters.Length;
                    offsets.Add(value, offset);
                    characters.Append(value);
                }
                BinaryPrimitives.WriteInt32LittleEndian(span[cursor..], offset);
                BinaryPrimitives.WriteInt32LittleEndian(span[(cursor + 4)..], value?.Length ?? -1);
                cursor += sizeof(int) * 2;
            }
        }
        return new(kind, [.. bytes, .. Encoding.Unicode.GetBytes(characters.ToString())]);
    }

    private static CompilerException Invariant(string message) =>
        new(new CompilerDiagnostic(DiagnosticCode.RuntimeContract, message));
}
