using System;
using System.Buffers;
using System.Collections.Generic;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// Replaces function bodies in a module this compiler wrote, leaving every other byte of
/// every other section as it was: only the code section is rebuilt, around the new bodies.
/// Bodies are named by their position among the module's defined functions (a function's
/// index less the module's function imports).
/// </summary>
internal static class WasmCodePatcher
{
    private const int CodeSection = 10;
    private const int FunctionSection = 3;

    // The patched module, or null when the module is malformed, has no code section, or
    // names fewer defined functions than a replacement needs.
    public static byte[]? ReplaceBodies(ReadOnlySpan<byte> module, IReadOnlyDictionary<int, byte[]> bodies)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        if (module.Length < 8 || !module[..4].SequenceEqual("\0asm"u8))
            return null;
        var output = new ArrayBufferWriter<byte>(module.Length + 1024);
        output.Write(module[..8]);
        var at = 8;
        var defined = -1;
        var patched = false;
        while (at < module.Length)
        {
            var start = at;
            var id = module[at++];
            if (!TryReadUnsigned(module, ref at, out var size) || size > (uint)(module.Length - at))
                return null;
            var payload = module.Slice(at, (int)size);
            at += (int)size;
            if (id == FunctionSection)
            {
                var cursor = 0;
                if (!TryReadUnsigned(payload, ref cursor, out var count)) return null;
                defined = (int)count;
            }
            if (id != CodeSection)
            {
                output.Write(module[start..at]);
                continue;
            }
            if (Rewrite(payload, defined, bodies) is not { } code)
                return null;
            output.Write<byte>([CodeSection]);
            WriteUnsigned(output, (uint)code.Length);
            output.Write(code);
            patched = true;
        }
        return patched ? output.WrittenSpan.ToArray() : null;
    }

    // The defined functions' bodies in order, or null for a malformed module.
    public static IReadOnlyList<byte[]>? Bodies(ReadOnlySpan<byte> module)
    {
        if (module.Length < 8 || !module[..4].SequenceEqual("\0asm"u8))
            return null;
        var at = 8;
        while (at < module.Length)
        {
            var id = module[at++];
            if (!TryReadUnsigned(module, ref at, out var size) || size > (uint)(module.Length - at))
                return null;
            var payload = module.Slice(at, (int)size);
            at += (int)size;
            if (id != CodeSection) continue;
            var cursor = 0;
            if (!TryReadUnsigned(payload, ref cursor, out var count)) return null;
            var bodies = new List<byte[]>((int)Math.Min(count, 1u << 20));
            for (var position = 0; position < (int)count; position++)
            {
                if (!TryReadUnsigned(payload, ref cursor, out var length) || length > (uint)(payload.Length - cursor))
                    return null;
                bodies.Add(payload.Slice(cursor, (int)length).ToArray());
                cursor += (int)length;
            }
            return bodies;
        }
        return null;
    }

    private static byte[]? Rewrite(ReadOnlySpan<byte> payload, int defined, IReadOnlyDictionary<int, byte[]> bodies)
    {
        var at = 0;
        if (!TryReadUnsigned(payload, ref at, out var count) || (defined >= 0 && count != (uint)defined))
            return null;
        foreach (var position in bodies.Keys)
        {
            if ((uint)position >= count) return null;
        }
        var output = new ArrayBufferWriter<byte>(payload.Length + 1024);
        WriteUnsigned(output, count);
        for (var position = 0; position < (int)count; position++)
        {
            var start = at;
            if (!TryReadUnsigned(payload, ref at, out var size) || size > (uint)(payload.Length - at))
                return null;
            at += (int)size;
            if (bodies.TryGetValue(position, out var body))
            {
                WriteUnsigned(output, (uint)body.Length);
                output.Write(body);
            }
            else
            {
                output.Write(payload[start..at]);
            }
        }
        return at == payload.Length ? output.WrittenSpan.ToArray() : null;
    }

    private static bool TryReadUnsigned(ReadOnlySpan<byte> bytes, ref int at, out uint value)
    {
        value = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            if (at >= bytes.Length) return false;
            var next = bytes[at++];
            value |= (uint)(next & 0x7F) << shift;
            if ((next & 0x80) == 0) return true;
        }
        return false;
    }

    private static void WriteUnsigned(ArrayBufferWriter<byte> output, uint value)
    {
        do
        {
            var next = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0) next |= 0x80;
            output.Write<byte>([next]);
        }
        while (value != 0);
    }
}
