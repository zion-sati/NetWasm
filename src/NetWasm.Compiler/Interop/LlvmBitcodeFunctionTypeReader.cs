// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Interop;

// Reads an LLVM bitcode module (an LTO archive's member) for the functions it defines and other modules can link to:
// each one's type from the module's type table, whether it is variadic, which a Wasm object's type cannot say, and its
// name from the string table. A declaration (a prototype) or a function with internal or private linkage defines
// nothing another module links to. Integers of up to 32 bits pass as 32 bits, as in Wasm's C ABI; pointers are native
// integers, whose width the target the archive was built for gives.
internal static class LlvmBitcodeFunctionTypeReader
{
    private const int ModuleBlock = 8;
    private const int TypeBlock = 17;
    private const int StringTableBlock = 23;
    private const int BlockInfoBlock = 0;

    private static readonly byte[] Magic = [0x42, 0x43, 0xc0, 0xde];
    private static readonly byte[] WrapperMagic = [0xde, 0xc0, 0x17, 0x0b];

    public static bool IsBitcode(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4 && (bytes[..4].SequenceEqual(Magic) || bytes[..4].SequenceEqual(WrapperMagic));

    public static void Read(ReadOnlySpan<byte> bytes, IDictionary<string, NativeFunctionType> functions)
    {
        if (bytes[..4].SequenceEqual(WrapperMagic))
        {
            // The wrapper header: magic, version, offset, size, CPU type.
            var offset = (int)BitConverter.ToUInt32(bytes[8..12]);
            var size = (int)BitConverter.ToUInt32(bytes[12..16]);
            bytes = bytes.Slice(offset, size);
        }
        var stream = new Bitstream(bytes.ToArray(), 32);
        var module = new ModuleFacts();
        byte[]? strings = null;
        var blockInfo = new Dictionary<int, List<Abbreviation>>();
        stream.ReadBlocks(blockInfo, (stream, block) =>
        {
            switch (block)
            {
                case ModuleBlock:
                    ReadModule(stream, blockInfo, module);
                    return true;
                case StringTableBlock:
                    stream.ReadRecords(blockInfo, StringTableBlock, null, record =>
                    {
                        if (record.Code == 1) strings = record.Blob;
                    });
                    return true;
                default:
                    return false;
            }
        });
        if (strings is null) return;
        foreach (var function in module.Functions)
        {
            if (function.Offset + function.Size > (ulong)strings.Length || function.Type >= (ulong)module.Types.Count)
                continue;
            var type = module.Types[(int)function.Type];
            // Bitcode before LLVM 15 gives a function a typed pointer to its function type.
            if (type.Pointee is { } pointee)
            {
                if (pointee >= (ulong)module.Types.Count) continue;
                type = module.Types[(int)pointee];
            }
            if (type.Function is not { } signature) continue;
            var name = Encoding.UTF8.GetString(strings, (int)function.Offset, (int)function.Size);
            var parameters = signature.Parameters.Select(parameter => Kind(module.Types, parameter));
            functions[name] = new NativeFunctionType([.. parameters], Kind(module.Types, signature.Result))
            {
                Variadic = signature.Variadic,
            };
        }
    }

    private static void ReadModule(Bitstream stream, Dictionary<int, List<Abbreviation>> blockInfo, ModuleFacts module)
    {
        stream.ReadRecords(blockInfo, ModuleBlock, (inner, block) =>
        {
            if (block != TypeBlock) return false;
            inner.ReadRecords(blockInfo, TypeBlock, null, record => ReadType(record, module.Types));
            return true;
        }, record =>
        {
            // MODULE_CODE_VERSION 2 names values in the string table; MODULE_CODE_FUNCTION's operands are then
            // [name offset, name size, type, calling convention, is prototype, linkage, ...].
            if (record.Code == 1 && record.Operands.Count > 0) module.Version = record.Operands[0];
            if (record.Code != 8 || module.Version < 2 || record.Operands.Count < 6) return;
            var operands = record.Operands;
            var linkage = operands[5];
            if (operands[4] != 0 || linkage is 3 or 9) return;
            module.Functions.Add(new(operands[0], operands[1], operands[2]));
        });
    }

    // One type table entry per record but NUMENTRY's and STRUCT_NAME's.
    private static void ReadType(Record record, List<TypeEntry> types)
    {
        var operands = record.Operands;
        switch (record.Code)
        {
            case 1 or 19:
                return;
            case 2:
                types.Add(new(CliValueKind.Void));
                return;
            case 3:
                types.Add(new(CliValueKind.F4));
                return;
            case 4:
                types.Add(new(CliValueKind.F8));
                return;
            case 7:
                types.Add(new(operands.Count > 0 && operands[0] <= 32 ? CliValueKind.I4
                    : operands.Count > 0 && operands[0] == 64 ? CliValueKind.I8 : CliValueKind.Unknown));
                return;
            case 8:
                types.Add(new(CliValueKind.NativeInt) { Pointee = operands.Count > 0 ? operands[0] : null });
                return;
            case 25:
                types.Add(new(CliValueKind.NativeInt));
                return;
            case 21 when operands.Count >= 2:
                types.Add(new(CliValueKind.Unknown)
                {
                    Function = new(operands[0] != 0, operands[1], [.. operands.Skip(2)]),
                });
                return;
            default:
                types.Add(new(CliValueKind.Unknown));
                return;
        }
    }

    private static CliValueKind Kind(List<TypeEntry> types, ulong index) =>
        index < (ulong)types.Count && types[(int)index].Function is null ? types[(int)index].Kind : CliValueKind.Unknown;

    private sealed class ModuleFacts
    {
        public ulong Version { get; set; }

        public List<TypeEntry> Types { get; } = [];

        public List<FunctionRecord> Functions { get; } = [];
    }

    private sealed record FunctionRecord(ulong Offset, ulong Size, ulong Type);

    private sealed record FunctionSignature(bool Variadic, ulong Result, ImmutableArray<ulong> Parameters);

    private sealed record TypeEntry(CliValueKind Kind)
    {
        public ulong? Pointee { get; init; }

        public FunctionSignature? Function { get; init; }
    }

    private sealed record Record(ulong Code, List<ulong> Operands, byte[]? Blob);

    private enum OperandEncoding { Literal, Fixed, Vbr, Array, Char6, Blob }

    private sealed record AbbreviationOperand(OperandEncoding Encoding, ulong Value);

    private sealed record Abbreviation(List<AbbreviationOperand> Operands);

    // LLVM's bitstream: little-endian bits in 32-bit words, blocks of records that abbreviations may compress.
    private sealed class Bitstream(byte[] bytes, int startBit)
    {
        private long _bit = startBit;
        private int _width = 2;

        private bool AtEnd => _bit + _width > (long)bytes.Length * 8;

        // The top level's blocks: those the visitor reads, and BLOCKINFO; every other block is skipped.
        internal void ReadBlocks(Dictionary<int, List<Abbreviation>> blockInfo, Func<Bitstream, int, bool> visit)
        {
            while (!AtEnd)
            {
                var id = (int)Fixed(_width);
                // Zero padding ends the stream; anything else at the top level but a block is malformed.
                if (id == 0) return;
                if (id != 1) throw new FormatException("bitcode has a top-level record");
                EnterBlock(blockInfo, visit);
            }
        }

        // A block's records, after ENTER_SUBBLOCK: nested blocks go to the visitor (which reads them or not), records
        // to the callback.
        internal void ReadRecords(Dictionary<int, List<Abbreviation>> blockInfo, int block,
            Func<Bitstream, int, bool>? visit, Action<Record> records)
        {
            var abbreviations = new List<Abbreviation>(blockInfo.GetValueOrDefault(block) ?? []);
            int? infoTarget = null;
            while (true)
            {
                var id = (int)Fixed(_width);
                switch (id)
                {
                    case 0:
                        Align32();
                        return;
                    case 1:
                        EnterBlock(blockInfo, visit ?? ((_, _) => false));
                        break;
                    case 2:
                        var abbreviation = DefineAbbreviation();
                        if (block == BlockInfoBlock)
                        {
                            if (infoTarget is not { } target) throw new FormatException("an abbreviation before SETBID");
                            if (!blockInfo.TryGetValue(target, out var list)) blockInfo[target] = list = [];
                            list.Add(abbreviation);
                        }
                        else abbreviations.Add(abbreviation);
                        break;
                    case 3:
                        var code = Vbr(6);
                        var count = Vbr(6);
                        var operands = new List<ulong>();
                        for (var index = 0UL; index < count; index++) operands.Add(Vbr(6));
                        Deliver(new(code, operands, null));
                        break;
                    default:
                        if (id - 4 >= abbreviations.Count) throw new FormatException("an unknown abbreviation");
                        Deliver(ReadAbbreviated(abbreviations[id - 4]));
                        break;
                }
            }

            void Deliver(Record record)
            {
                if (block == BlockInfoBlock)
                {
                    if (record.Code == 1 && record.Operands.Count > 0) infoTarget = (int)record.Operands[0];
                }
                else records(record);
            }
        }

        private void EnterBlock(Dictionary<int, List<Abbreviation>> blockInfo, Func<Bitstream, int, bool> visit)
        {
            var block = (int)Vbr(8);
            var width = (int)Vbr(4);
            Align32();
            var words = Fixed(32);
            var end = _bit + (long)words * 32;
            var outer = _width;
            _width = width;
            if (block == BlockInfoBlock) ReadRecords(blockInfo, BlockInfoBlock, null, _ => { });
            else if (!visit(this, block)) _bit = end;
            _width = outer;
            if (_bit != end) _bit = end;
        }

        private Abbreviation DefineAbbreviation()
        {
            var count = Vbr(5);
            var operands = new List<AbbreviationOperand>();
            for (var index = 0UL; index < count; index++)
            {
                if (Fixed(1) == 1)
                {
                    operands.Add(new(OperandEncoding.Literal, Vbr(8)));
                    continue;
                }
                var encoding = Fixed(3) switch
                {
                    1 => OperandEncoding.Fixed,
                    2 => OperandEncoding.Vbr,
                    3 => OperandEncoding.Array,
                    4 => OperandEncoding.Char6,
                    5 => OperandEncoding.Blob,
                    _ => throw new FormatException("an unknown abbreviation encoding"),
                };
                operands.Add(new(encoding, encoding is OperandEncoding.Fixed or OperandEncoding.Vbr ? Vbr(5) : 0));
            }
            return new(operands);
        }

        private Record ReadAbbreviated(Abbreviation abbreviation)
        {
            var values = new List<ulong>();
            byte[]? blob = null;
            for (var index = 0; index < abbreviation.Operands.Count; index++)
            {
                var operand = abbreviation.Operands[index];
                switch (operand.Encoding)
                {
                    case OperandEncoding.Array:
                        var element = abbreviation.Operands[++index];
                        for (var count = Vbr(6); count > 0; count--) values.Add(Scalar(element));
                        break;
                    case OperandEncoding.Blob:
                        var length = (int)Vbr(6);
                        Align32();
                        blob = new byte[length];
                        for (var at = 0; at < length; at++) blob[at] = (byte)Fixed(8);
                        Align32();
                        break;
                    default:
                        values.Add(Scalar(operand));
                        break;
                }
            }
            if (values.Count == 0) throw new FormatException("an abbreviated record without a code");
            return new(values[0], values.GetRange(1, values.Count - 1), blob);
        }

        private ulong Scalar(AbbreviationOperand operand) => operand.Encoding switch
        {
            OperandEncoding.Literal => operand.Value,
            OperandEncoding.Fixed => Fixed((int)operand.Value),
            OperandEncoding.Vbr => Vbr((int)operand.Value),
            OperandEncoding.Char6 => Fixed(6),
            _ => throw new FormatException("an array of arrays or blobs"),
        };

        private ulong Fixed(int width)
        {
            if (width is < 0 or > 64) throw new FormatException("a field wider than 64 bits");
            if (_bit + width > (long)bytes.Length * 8) throw new FormatException("bitcode ends inside a field");
            ulong value = 0;
            for (var index = 0; index < width; index++, _bit++)
                value |= (ulong)((bytes[_bit >> 3] >> (int)(_bit & 7)) & 1) << index;
            return value;
        }

        private ulong Vbr(int width)
        {
            if (width < 2) throw new FormatException("a VBR field narrower than 2 bits");
            ulong value = 0;
            var shift = 0;
            while (true)
            {
                var piece = Fixed(width);
                var continues = (piece >> (width - 1)) != 0;
                if (shift > 63) throw new FormatException("a VBR value longer than 64 bits");
                value |= (piece & ((1UL << (width - 1)) - 1)) << shift;
                if (!continues) return value;
                shift += width - 1;
            }
        }

        private void Align32() => _bit = (_bit + 31) & ~31L;
    }
}
