using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class LinkedRuntimeModuleReader : ILinkedRuntimeModuleReader
{
    private const long WasmPageSize = 65_536;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public RuntimeLinkedModule Read(ReadOnlyMemory<byte> module)
    {
        try
        {
            var reader = new SectionReader(module.Span);
            if (!reader.Bytes(8).SequenceEqual("\0asm\x01\0\0\0"u8))
                throw Invalid();
            var globals = new List<GlobalValue>();
            var types = ImmutableArray.CreateBuilder<RuntimeLinkedFunctionType>();
            var importedFunctions = ImmutableArray.CreateBuilder<uint>();
            var imports = ImmutableArray.CreateBuilder<RuntimeLinkedImport>();
            var definedFunctions = ImmutableArray.CreateBuilder<uint>();
            var exports = new Dictionary<string, (byte Kind, uint Index)>(StringComparer.Ordinal);
            var sections = new HashSet<byte>();
            (bool Memory64, ulong Minimum, ulong Maximum)? memory = null;
            while (reader.Remaining != 0)
            {
                var id = reader.Byte();
                var section = new SectionReader(reader.Bytes(checked((int)reader.Unsigned(32))));
                if (id is not (1 or 2 or 3 or 5 or 6 or 7))
                    continue;
                if (!sections.Add(id))
                    throw Invalid();
                var count = section.Unsigned(32);
                for (ulong index = 0; index < count; index++)
                {
                    switch (id)
                    {
                        case 1:
                            if (section.Byte() != 0x60)
                                throw Invalid();
                            var parameters = section.Bytes(checked((int)section.Unsigned(32))).ToArray().ToImmutableArray();
                            var results = section.Bytes(checked((int)section.Unsigned(32))).ToArray().ToImmutableArray();
                            types.Add(new(parameters, results));
                            break;
                        case 2:
                            var importModule = section.Name();
                            var importName = section.Name();
                            var kind = section.Byte();
                            if (kind == 0)
                            {
                                var typeIndex = checked((uint)section.Unsigned(32));
                                importedFunctions.Add(typeIndex);
                                imports.Add(new(importModule, importName, kind, typeIndex));
                            }
                            else if (kind == 4)
                            {
                                if (section.Byte() != 0)
                                    throw Invalid();
                                imports.Add(new(importModule, importName, kind, checked((uint)section.Unsigned(32))));
                            }
                            else
                                throw Invalid(); // Linked native runtimes own their memory/table/globals.
                            break;
                        case 3:
                            definedFunctions.Add(checked((uint)section.Unsigned(32)));
                            break;
                        case 5:
                            if (memory.HasValue)
                                throw Invalid();
                            var flags = section.Unsigned(32);
                            if (flags is not (1 or 5))
                                throw Invalid(); // Require a maximum; shared/custom-page memories are outside this profile.
                            var memory64 = flags == 5;
                            var width = memory64 ? 64 : 32;
                            memory = (memory64, section.Unsigned(width), section.Unsigned(width));
                            break;
                        case 6:
                            var type = section.Byte();
                            var mutable = section.Byte();
                            if (mutable > 1)
                                throw Invalid();
                            var opcode = section.Byte();
                            long? value = null;
                            switch (opcode)
                            {
                                case 0x41:
                                    if (type != 0x7f)
                                        throw Invalid();
                                    value = unchecked((uint)section.Signed(32));
                                    break;
                                case 0x42:
                                    if (type != 0x7e)
                                        throw Invalid();
                                    value = section.Signed(64);
                                    break;
                                case 0x43:
                                    if (type != 0x7d)
                                        throw Invalid();
                                    section.Bytes(sizeof(float));
                                    break;
                                case 0x44:
                                    if (type != 0x7c)
                                        throw Invalid();
                                    section.Bytes(sizeof(double));
                                    break;
                                default:
                                    throw Invalid();
                            }
                            if (section.Byte() != 0x0b)
                                throw Invalid();
                            globals.Add(new(type, mutable != 0, value));
                            break;
                        case 7:
                            var name = section.Name();
                            var exportKind = section.Byte();
                            var exportIndex = checked((uint)section.Unsigned(32));
                            if (!exports.TryAdd(name, (exportKind, exportIndex)))
                                throw Invalid();
                            break;
                    }
                }
                if (section.Remaining != 0)
                    throw Invalid();
            }
            if (memory is not { } limits || limits.Minimum > limits.Maximum)
                throw Invalid();
            var expectedType = limits.Memory64 ? (byte)0x7e : (byte)0x7f;
            long Bound(string name)
            {
                if (!exports.TryGetValue(name, out var export) || export.Kind != 3 ||
                    export.Index >= globals.Count)
                    throw Invalid();
                var global = globals[(int)export.Index];
                if (global.Type != expectedType || global.Mutable || global.Value is not >= 0)
                    throw Invalid();
                return global.Value.Value;
            }
            var layout = new RuntimeLinkedMemoryLayout(limits.Memory64 ? "wasm64" : "wasm32",
                Bound("__global_base"), Bound("__data_end"), Bound("__stack_low"),
                Bound("__stack_high"), Bound("__heap_base"),
                checked((long)limits.Minimum * WasmPageSize),
                checked((long)limits.Maximum * WasmPageSize));
            return new(layout, types.ToImmutable(), importedFunctions.ToImmutable(), definedFunctions.ToImmutable(),
                [.. exports.Select(pair => new RuntimeLinkedExport(pair.Key, pair.Value.Kind, pair.Value.Index))])
            { Imports = imports.ToImmutable() };
        }
        catch (Exception exception) when (exception is OverflowException or DecoderFallbackException)
        {
            throw Invalid(exception);
        }
    }

    private static InvalidOperationException Invalid(Exception? cause = null) =>
        new("The native-linked Wasm module authority is missing or invalid.", cause);

    private readonly record struct GlobalValue(byte Type, bool Mutable, long? Value);

    private ref struct SectionReader(ReadOnlySpan<byte> bytes)
    {
        private ReadOnlySpan<byte> _remaining = bytes;
        public readonly int Remaining => _remaining.Length;

        public byte Byte()
        {
            if (_remaining.IsEmpty)
                throw Invalid();
            var result = _remaining[0];
            _remaining = _remaining[1..];
            return result;
        }

        public ReadOnlySpan<byte> Bytes(int length)
        {
            if (length < 0 || length > _remaining.Length)
                throw Invalid();
            var result = _remaining[..length];
            _remaining = _remaining[length..];
            return result;
        }

        public string Name() => Utf8.GetString(Bytes(checked((int)Unsigned(32))));

        public ulong Unsigned(int bits)
        {
            ulong value = 0;
            for (var shift = 0; shift < bits; shift += 7)
            {
                var item = Byte();
                var payload = item & 0x7f;
                if (bits - shift < 7 && payload >= 1 << (bits - shift))
                    throw Invalid();
                value |= (ulong)payload << shift;
                if ((item & 0x80) == 0)
                    return value;
            }
            throw Invalid();
        }

        public long Signed(int bits)
        {
            ulong value = 0;
            for (var shift = 0; shift < bits; shift += 7)
            {
                var item = Byte();
                var payload = item & 0x7f;
                var remainingBits = bits - shift;
                if (remainingBits < 7)
                {
                    var unusedMask = 0x7f & ~((1 << remainingBits) - 1);
                    var negative = (payload & (1 << (remainingBits - 1))) != 0;
                    if ((payload & unusedMask) != (negative ? unusedMask : 0))
                        throw Invalid();
                }
                value |= (ulong)payload << shift;
                if ((item & 0x80) != 0)
                    continue;
                var consumed = shift + 7;
                if ((payload & 0x40) != 0 && consumed < 64)
                    value |= ulong.MaxValue << consumed;
                var signed = unchecked((long)value);
                return bits == 32 ? unchecked((int)signed) : signed;
            }
            throw Invalid();
        }
    }
}
