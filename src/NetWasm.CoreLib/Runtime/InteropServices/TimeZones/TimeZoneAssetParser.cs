namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class TimeZoneAssetParser(
        ITimeZoneAssetDigestCalculator digests) : ITimeZoneAssetParser
    {
        private const int DigestLength = 32;
        private const int MaximumOffsetSeconds = 24 * 60 * 60;
        private static readonly byte[] Magic = { 0x4e, 0x57, 0x54, 0x5a, 1, 0, 0, 0 };
        private readonly ITimeZoneAssetDigestCalculator _digests =
            digests ?? throw new ArgumentNullException();

        public TimeZoneAsset Parse(byte[] contents)
        {
            if (contents == null)
            {
                throw new ArgumentNullException();
            }
            if (contents.Length < Magic.Length + 2 + 4 + DigestLength)
            {
                throw Invalid("asset is truncated");
            }
            var payloadLength = contents.Length - DigestLength;
            var actualDigest = _digests.Calculate(contents, payloadLength);
            for (var index = 0; index < DigestLength; index++)
            {
                if (actualDigest[index] != contents[payloadLength + index])
                {
                    throw Invalid("integrity digest does not match the payload");
                }
            }

            var offset = 0;
            for (var index = 0; index < Magic.Length; index++)
            {
                if (ReadByte(contents, payloadLength, ref offset) != Magic[index])
                {
                    throw Invalid("asset format version is incompatible");
                }
            }
            var dataVersion = ReadText(
                contents,
                payloadLength,
                ref offset,
                ReadUInt16(contents, payloadLength, ref offset),
                "IANA version");
            if (dataVersion.Length == 0)
            {
                throw Invalid("IANA version is empty");
            }
            var zoneCount = ReadCount(contents, payloadLength, ref offset, "zone count");
            if (zoneCount == 0)
            {
                throw Invalid("asset contains no zones");
            }
            var zones = new TimeZoneDefinition[zoneCount];
            for (var zoneIndex = 0; zoneIndex < zones.Length; zoneIndex++)
            {
                var name = ReadText(
                    contents,
                    payloadLength,
                    ref offset,
                    ReadUInt16(contents, payloadLength, ref offset),
                    "zone name");
                if (name.Length == 0 || name[0] == '/' || name.Contains(".."))
                {
                    throw Invalid("zone name is not normalized");
                }
                for (var previous = 0; previous < zoneIndex; previous++)
                {
                    if (zones[previous].Name == name)
                    {
                        throw Invalid("asset contains a duplicate zone");
                    }
                }
                var initialOffset = ReadOffset(contents, payloadLength, ref offset);
                var initialDaylight = ReadBoolean(contents, payloadLength, ref offset);
                var transitionCount = ReadCount(
                    contents,
                    payloadLength,
                    ref offset,
                    "transition count");
                if (transitionCount > (payloadLength - offset) / 13)
                {
                    throw Invalid("transition table is truncated");
                }
                var transitions = new TimeZoneTransition[transitionCount];
                var previousSeconds = long.MinValue;
                for (var transitionIndex = 0;
                    transitionIndex < transitions.Length;
                    transitionIndex++)
                {
                    var seconds = ReadInt64(contents, payloadLength, ref offset);
                    if (seconds <= previousSeconds)
                    {
                        throw Invalid("transitions are not strictly ordered");
                    }
                    previousSeconds = seconds;
                    transitions[transitionIndex] = new TimeZoneTransition(
                        seconds,
                        ReadOffset(contents, payloadLength, ref offset),
                        ReadBoolean(contents, payloadLength, ref offset));
                }
                zones[zoneIndex] = new TimeZoneDefinition(
                    name,
                    initialOffset,
                    initialDaylight,
                    transitions);
            }
            if (offset != payloadLength)
            {
                throw Invalid("asset contains trailing payload data");
            }
            return new TimeZoneAsset(dataVersion, ToHex(actualDigest), zones);
        }

        private static byte ReadByte(byte[] contents, int limit, ref int offset)
        {
            if (offset >= limit)
            {
                throw Invalid("asset is truncated");
            }
            return contents[offset++];
        }

        private static ushort ReadUInt16(byte[] contents, int limit, ref int offset)
        {
            var low = ReadByte(contents, limit, ref offset);
            var high = ReadByte(contents, limit, ref offset);
            return (ushort)(low | high << 8);
        }

        private static int ReadInt32(byte[] contents, int limit, ref int offset)
        {
            var value = ReadByte(contents, limit, ref offset) |
                ReadByte(contents, limit, ref offset) << 8 |
                ReadByte(contents, limit, ref offset) << 16 |
                ReadByte(contents, limit, ref offset) << 24;
            return value;
        }

        private static long ReadInt64(byte[] contents, int limit, ref int offset)
        {
            var low = unchecked((uint)ReadInt32(contents, limit, ref offset));
            var high = ReadInt32(contents, limit, ref offset);
            return unchecked((long)((ulong)(uint)high << 32 | low));
        }

        private static int ReadCount(
            byte[] contents,
            int limit,
            ref int offset,
            string field)
        {
            var value = ReadInt32(contents, limit, ref offset);
            if (value < 0)
            {
                throw Invalid(field + " exceeds the supported address space");
            }
            return value;
        }

        private static int ReadOffset(byte[] contents, int limit, ref int offset)
        {
            var value = ReadInt32(contents, limit, ref offset);
            if (value is < -MaximumOffsetSeconds or > MaximumOffsetSeconds)
            {
                throw Invalid("UTC offset is outside the supported range");
            }
            return value;
        }

        private static bool ReadBoolean(byte[] contents, int limit, ref int offset) =>
            ReadByte(contents, limit, ref offset) switch
            {
                0 => false,
                1 => true,
                _ => throw Invalid("daylight-saving flag is invalid"),
            };

        private static string ReadText(
            byte[] contents,
            int limit,
            ref int offset,
            int length,
            string field)
        {
            if (length > limit - offset)
            {
                throw Invalid(field + " is truncated");
            }
            var characters = new char[length];
            for (var index = 0; index < length; index++)
            {
                var value = contents[offset++];
                if (value is < 0x21 or > 0x7e)
                {
                    throw Invalid(field + " is not printable ASCII");
                }
                characters[index] = (char)value;
            }
            return new string(characters);
        }

        private static string ToHex(byte[] bytes)
        {
            const string alphabet = "0123456789abcdef";
            var characters = new char[bytes.Length * 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                characters[index * 2] = alphabet[bytes[index] >> 4];
                characters[index * 2 + 1] = alphabet[bytes[index] & 15];
            }
            return new string(characters);
        }

        private static TimeZoneAssetValidationException Invalid(string reason) =>
            new(reason);
    }
}
