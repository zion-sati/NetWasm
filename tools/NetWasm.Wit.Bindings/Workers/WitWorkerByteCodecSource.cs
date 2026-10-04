namespace NetWasm.Wit.Bindings.Workers;

internal static class WitWorkerByteCodecSource
{
    internal const string CSharp = """
        private sealed class WireWriter
        {
            internal readonly System.Collections.Generic.List<byte> Data = new();
            internal byte[] Bytes => Data.ToArray();
        }
        private sealed class WireReader
        {
            internal readonly byte[] Data;
            internal int Offset;
            internal WireReader(byte[] bytes) => Data = bytes;
            internal int Remaining => Data.Length - Offset;
            internal void Require(int length) { if (length < 0 || length > Remaining) throw InvalidWire(); }
            internal int Count() { var count = Readu32(this); if (count > (uint)Remaining) throw InvalidWire(); return (int)count; }
            internal void End() { if (Remaining != 0) throw InvalidWire(); }
        }
        private static Exception InvalidWire() => new InvalidOperationException("Worker byte payload is invalid.");
        private static void Writeu8(WireWriter writer, byte value) => writer.Data.Add(value);
        private static byte Readu8(WireReader reader) { reader.Require(1); return reader.Data[reader.Offset++]; }
        private static void Writes8(WireWriter writer, sbyte value) => Writeu8(writer, unchecked((byte)value));
        private static sbyte Reads8(WireReader reader) => unchecked((sbyte)Readu8(reader));
        private static void Writeu16(WireWriter writer, ushort value) { for (var index = 0; index < 2; index++) Writeu8(writer, (byte)(value >> (8 * index))); }
        private static ushort Readu16(WireReader reader) { uint value = 0; for (var index = 0; index < 2; index++) value |= (uint)Readu8(reader) << (8 * index); return (ushort)value; }
        private static void Writes16(WireWriter writer, short value) => Writeu16(writer, unchecked((ushort)value));
        private static short Reads16(WireReader reader) => unchecked((short)Readu16(reader));
        private static void Writeu32(WireWriter writer, uint value) { for (var index = 0; index < 4; index++) Writeu8(writer, (byte)(value >> (8 * index))); }
        private static uint Readu32(WireReader reader) { uint value = 0; for (var index = 0; index < 4; index++) value |= (uint)Readu8(reader) << (8 * index); return value; }
        private static void Writes32(WireWriter writer, int value) => Writeu32(writer, unchecked((uint)value));
        private static int Reads32(WireReader reader) => unchecked((int)Readu32(reader));
        private static void Writeu64(WireWriter writer, ulong value) { for (var index = 0; index < 8; index++) Writeu8(writer, (byte)(value >> (8 * index))); }
        private static ulong Readu64(WireReader reader) { ulong value = 0; for (var index = 0; index < 8; index++) value |= (ulong)Readu8(reader) << (8 * index); return value; }
        private static void Writes64(WireWriter writer, long value) => Writeu64(writer, unchecked((ulong)value));
        private static long Reads64(WireReader reader) => unchecked((long)Readu64(reader));
        private static void Writebool(WireWriter writer, bool value) => Writeu8(writer, value ? (byte)1 : (byte)0);
        private static bool Readbool(WireReader reader) { var value = Readu8(reader); if (value > 1) throw InvalidWire(); return value != 0; }
        private static void Writef32(WireWriter writer, float value) => Writes32(writer, BitConverter.SingleToInt32Bits(value));
        private static float Readf32(WireReader reader) => BitConverter.Int32BitsToSingle(Reads32(reader));
        private static void Writef64(WireWriter writer, double value) => Writes64(writer, BitConverter.DoubleToInt64Bits(value));
        private static double Readf64(WireReader reader) => BitConverter.Int64BitsToDouble(Reads64(reader));
        private static void Writechar(WireWriter writer, uint value) { if (value > 0x10ffffu || value is >= 0xd800u and <= 0xdfffu) throw InvalidWire(); Writeu32(writer, value); }
        private static uint Readchar(WireReader reader) { var value = Readu32(reader); if (value > 0x10ffffu || value is >= 0xd800u and <= 0xdfffu) throw InvalidWire(); return value; }
        private static void Writestring(WireWriter writer, string value)
        {
            var bytes = new System.Text.UTF8Encoding(false, true).GetBytes(value);
            Writeu32(writer, checked((uint)bytes.Length)); writer.Data.AddRange(bytes);
        }
        private static string Readstring(WireReader reader)
        {
            var length = reader.Count();
            var value = new System.Text.UTF8Encoding(false, true).GetString(reader.Data, reader.Offset, length);
            reader.Offset += length; return value;
        }
        private static void Readunit(WireReader reader) { if (Readu8(reader) != 0) throw InvalidWire(); }
        """;

    internal const string JavaScript = """
        function invalidWire() { return new TypeError('Worker byte payload is invalid.'); }
        class WireWriter {
          data = [];
          get bytes() { return new Uint8Array(this.data); }
        }
        class WireReader {
          constructor(bytes) { if (!(bytes instanceof Uint8Array)) throw invalidWire(); this.data = bytes; this.offset = 0; }
          get remaining() { return this.data.length - this.offset; }
          require(length) { if (!Number.isInteger(length) || length < 0 || length > this.remaining) throw invalidWire(); }
          count() { const count = readu32(this); this.require(count); return count; }
          end() { if (this.remaining !== 0) throw invalidWire(); }
        }
        function writeu8(writer, value) { writer.data.push(value); }
        function readu8(reader) { reader.require(1); return reader.data[reader.offset++]; }
        function writes8(writer, value) { writeu8(writer, value & 255); }
        function reads8(reader) { return (readu8(reader) << 24) >> 24; }
        function writeu16(writer, value) { for (let index = 0; index < 2; index++) writeu8(writer, (value >>> (index * 8)) & 255); }
        function readu16(reader) { let value = 0; for (let index = 0; index < 2; index++) value |= readu8(reader) << (index * 8); return value; }
        function writes16(writer, value) { writeu16(writer, value & 65535); }
        function reads16(reader) { return (readu16(reader) << 16) >> 16; }
        function writeu32(writer, value) { for (let index = 0; index < 4; index++) writeu8(writer, (value >>> (index * 8)) & 255); }
        function readu32(reader) { let value = 0; for (let index = 0; index < 4; index++) value |= readu8(reader) << (index * 8); return value >>> 0; }
        const writes32 = writeu32;
        function reads32(reader) { return readu32(reader) | 0; }
        function writeu64(writer, value) { value = BigInt.asUintN(64, value); for (let index = 0n; index < 8n; index++) writeu8(writer, Number((value >> (index * 8n)) & 255n)); }
        function readu64(reader) { let value = 0n; for (let index = 0n; index < 8n; index++) value |= BigInt(readu8(reader)) << (index * 8n); return value; }
        const writes64 = writeu64;
        function reads64(reader) { return BigInt.asIntN(64, readu64(reader)); }
        function writebool(writer, value) { writeu8(writer, value ? 1 : 0); }
        function readbool(reader) { const value = readu8(reader); if (value > 1) throw invalidWire(); return value !== 0; }
        function writef32(writer, value) { const bytes = new Uint8Array(4); new DataView(bytes.buffer).setFloat32(0, value, true); for (const byte of bytes) writeu8(writer, byte); }
        function readf32(reader) { reader.require(4); const value = new DataView(reader.data.buffer, reader.data.byteOffset + reader.offset, 4).getFloat32(0, true); reader.offset += 4; return value; }
        function writef64(writer, value) { const bytes = new Uint8Array(8); new DataView(bytes.buffer).setFloat64(0, value, true); for (const byte of bytes) writeu8(writer, byte); }
        function readf64(reader) { reader.require(8); const value = new DataView(reader.data.buffer, reader.data.byteOffset + reader.offset, 8).getFloat64(0, true); reader.offset += 8; return value; }
        function writechar(writer, value) { writeu32(writer, value.codePointAt(0)); }
        function readchar(reader) { const value = readu32(reader); if (value > 0x10ffff || (value >= 0xd800 && value <= 0xdfff)) throw invalidWire(); return String.fromCodePoint(value); }
        function writestring(writer, value) { const bytes = new TextEncoder().encode(value); writeu32(writer, bytes.length); for (const byte of bytes) writeu8(writer, byte); }
        function readstring(reader) { const count = reader.count(); const value = new TextDecoder('utf-8', { fatal: true }).decode(reader.data.subarray(reader.offset, reader.offset + count)); reader.offset += count; return value; }
        function readunit(reader) { if (readu8(reader) !== 0) throw invalidWire(); }
        """;
}
