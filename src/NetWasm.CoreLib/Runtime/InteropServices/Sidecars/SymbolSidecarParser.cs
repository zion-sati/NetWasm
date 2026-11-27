// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Sidecars
{
    using System.Buffers.Binary;

    // Checks a whole sidecar before anything reads it: its header, kind and build ID, and that every entry and
    // string lies inside the file.
    internal sealed class SymbolSidecarParser : ISymbolSidecarParser
    {
        public SymbolSidecar Parse(byte[] bytes, ushort kind, string buildId)
        {
            if (bytes == null || buildId == null)
            {
                throw new ArgumentNullException();
            }
            var span = bytes.AsSpan();
            if (span.Length < SymbolSidecar.HeaderSize ||
                BinaryPrimitives.ReadUInt32LittleEndian(span) != SymbolSidecar.Magic)
            {
                throw new FormatException("it is not a NetWasm symbols file");
            }
            if (BinaryPrimitives.ReadUInt16LittleEndian(span[4..]) != SymbolSidecar.Version ||
                BinaryPrimitives.ReadUInt16LittleEndian(span[6..]) != kind)
            {
                throw new FormatException("it is another version or kind of symbols file");
            }
            if (!HasBuildId(span, buildId))
            {
                throw new FormatException("it is from another build of the program");
            }
            var count = BinaryPrimitives.ReadInt32LittleEndian(span[(8 + SymbolSidecar.BuildIdLength * sizeof(char))..]);
            var stringsPerEntry = BinaryPrimitives.ReadInt32LittleEndian(
                span[(12 + SymbolSidecar.BuildIdLength * sizeof(char))..]);
            var expected = kind == SymbolSidecar.TypeKind ? 4 : 1;
            var entrySize = sizeof(int) + expected * sizeof(int) * 2L;
            if (stringsPerEntry != expected || count < 0 ||
                SymbolSidecar.HeaderSize + count * entrySize > span.Length ||
                (span.Length - (SymbolSidecar.HeaderSize + count * entrySize)) % sizeof(char) != 0)
            {
                throw new FormatException("its entries do not fit the file");
            }
            // The entries fit the file, so the sidecar reads their IDs and string fields without checking each read.
            var sidecar = new SymbolSidecar(bytes, count, stringsPerEntry);
            for (var entry = 0; entry < count; entry++)
            {
                if (sidecar.Id(entry) < 0 || (entry > 0 && sidecar.Id(entry) <= sidecar.Id(entry - 1)))
                {
                    throw new FormatException("its IDs are not in order");
                }
                for (var index = 0; index < stringsPerEntry; index++)
                {
                    if (!sidecar.StringFits(entry, index))
                    {
                        throw new FormatException("a string lies outside the file");
                    }
                }
            }
            return sidecar;
        }

        // The file's build ID, UTF-16 after the kind, compared a character at a time.
        private static bool HasBuildId(ReadOnlySpan<byte> file, string buildId)
        {
            if (buildId.Length != SymbolSidecar.BuildIdLength)
            {
                return false;
            }
            for (var index = 0; index < SymbolSidecar.BuildIdLength; index++)
            {
                if (BinaryPrimitives.ReadUInt16LittleEndian(file[(8 + index * sizeof(char))..]) != buildId[index])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
