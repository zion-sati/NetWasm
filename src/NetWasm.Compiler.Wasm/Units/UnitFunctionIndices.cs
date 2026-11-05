using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// Each method's function index in a compile's module, by method identity, and how many
/// functions the module imports (a defined function's code entry is its index less those).
/// </summary>
internal sealed record UnitFunctionIndices(FrozenDictionary<string, int> Functions, int ImportedFunctions)
{
    private const uint Magic = 0x49464E4E; // "NNFI"
    private const int Version = 2;

    public static UnitFunctionIndices Empty { get; } = new(FrozenDictionary<string, int>.Empty, 0);

    public static byte[] Write(IReadOnlyCollection<KeyValuePair<string, int>> functions, int importedFunctions)
    {
        ArgumentNullException.ThrowIfNull(functions);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(importedFunctions);
            writer.Write(functions.Count);
            foreach (var (identity, index) in functions)
            {
                writer.Write(identity);
                writer.Write(index);
            }
        }
        return stream.ToArray();
    }

    // The indices written, or none for bytes of another format.
    public static UnitFunctionIndices Read(ReadOnlySpan<byte> bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                return Empty;
            var imported = reader.ReadInt32();
            var count = reader.ReadInt32();
            if (count < 0 || imported < 0) return Empty;
            var functions = new Dictionary<string, int>(count, StringComparer.Ordinal);
            for (var index = 0; index < count; index++) functions[reader.ReadString()] = reader.ReadInt32();
            return new(functions.ToFrozenDictionary(StringComparer.Ordinal), imported);
        }
        catch (EndOfStreamException)
        {
            return Empty;
        }
    }
}
