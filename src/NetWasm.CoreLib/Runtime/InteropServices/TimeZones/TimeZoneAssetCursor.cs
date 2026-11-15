// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.TimeZones
{
    // Reads the little-endian asset encoding through a window, so a scan of the zone index
    // reads a few kilobytes at a time rather than the whole asset.
    internal sealed class TimeZoneAssetCursor
    {
        internal const int TransitionLength = 13;
        private const int MinimumWindowLength = 4 * 1024;
        private const int MaximumOffsetSeconds = 24 * 60 * 60;
        private readonly ITimeZoneAssetFile _file;
        private byte[] _window = Array.Empty<byte>();
        private long _windowOffset;

        internal TimeZoneAssetCursor(ITimeZoneAssetFile file, long position)
        {
            _file = file ?? throw new ArgumentNullException();
            if (position < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            Position = position;
        }

        internal long Position { get; private set; }

        internal void Skip(long length) => Position = checked(Position + length);

        // Buffers length bytes with a single file read, failing with reason if the asset ends first.
        internal void Fill(int length, string reason)
        {
            var start = Position - _windowOffset;
            if (start >= 0 && start <= _window.Length - length)
            {
                return;
            }
            _window = _file.Read(Position, Math.Max(length, MinimumWindowLength));
            _windowOffset = Position;
            if (_window.Length < length)
            {
                throw Invalid(reason);
            }
        }

        internal byte ReadByte()
        {
            Fill(1, "asset is truncated");
            return _window[unchecked((int)(Position++ - _windowOffset))];
        }

        internal ushort ReadUInt16()
        {
            var low = ReadByte();
            var high = ReadByte();
            return (ushort)(low | high << 8);
        }

        internal int ReadInt32() =>
            ReadByte() | ReadByte() << 8 | ReadByte() << 16 | ReadByte() << 24;

        internal long ReadInt64()
        {
            var low = unchecked((uint)ReadInt32());
            var high = ReadInt32();
            return unchecked((long)((ulong)(uint)high << 32 | low));
        }

        internal int ReadCount(string field)
        {
            var value = ReadInt32();
            if (value < 0)
            {
                throw Invalid(field + " exceeds the supported address space");
            }
            return value;
        }

        internal int ReadOffset()
        {
            var value = ReadInt32();
            if (value is < -MaximumOffsetSeconds or > MaximumOffsetSeconds)
            {
                throw Invalid("UTC offset is outside the supported range");
            }
            return value;
        }

        internal bool ReadBoolean() =>
            ReadByte() switch
            {
                0 => false,
                1 => true,
                _ => throw Invalid("daylight-saving flag is invalid"),
            };

        internal string ReadText(int length, string field)
        {
            Fill(length, field + " is truncated");
            var characters = new char[length];
            for (var index = 0; index < length; index++)
            {
                var value = ReadByte();
                if (value is < 0x21 or > 0x7e)
                {
                    throw Invalid(field + " is not printable ASCII");
                }
                characters[index] = (char)value;
            }
            return new string(characters);
        }

        internal static TimeZoneAssetValidationException Invalid(string reason) =>
            new(reason);
    }
}
