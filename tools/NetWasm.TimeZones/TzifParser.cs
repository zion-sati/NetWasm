using System.Collections.Immutable;
using System.Text;

namespace NetWasm.TimeZones;

internal sealed class TzifParser(IPosixFutureRuleExpander futureRules) : ITzifParser
{
    private const int HeaderLength = 44;
    private const long MinimumUnixSeconds = -62_135_596_800;
    private const long MaximumUnixSeconds = 253_402_300_799;
    private readonly IPosixFutureRuleExpander _futureRules =
        futureRules ?? throw new ArgumentNullException(nameof(futureRules));

    public TimeZoneDefinition Parse(string name, byte[] contents)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(contents);
        var first = ReadHeader(contents, 0);
        var timeSize = 4;
        var blockOffset = HeaderLength;
        var header = first;
        if (first.Version is '2' or '3' or '4')
        {
            var secondHeaderOffset = checked(HeaderLength + BlockLength(first, 4));
            header = ReadHeader(contents, secondHeaderOffset);
            timeSize = 8;
            blockOffset = checked(secondHeaderOffset + HeaderLength);
        }
        var blockLength = BlockLength(header, timeSize);
        Require(contents, blockOffset, blockLength);
        var transitions = new long[header.TransitionCount];
        var offset = blockOffset;
        for (var index = 0; index < transitions.Length; index++)
        {
            transitions[index] = timeSize == 8
                ? ReadInt64(contents, ref offset)
                : ReadInt32(contents, ref offset);
        }
        var typeIndices = new byte[header.TransitionCount];
        Array.Copy(contents, offset, typeIndices, 0, typeIndices.Length);
        offset += typeIndices.Length;
        var types = new TzifType[header.TypeCount];
        for (var index = 0; index < types.Length; index++)
        {
            types[index] = new TzifType(
                ReadInt32(contents, ref offset),
                ReadByte(contents, ref offset) != 0);
            _ = ReadByte(contents, ref offset);
        }
        var initialType = Array.FindIndex(types, static type => !type.Daylight);
        if (initialType < 0)
        {
            initialType = 0;
        }
        var selectedType = initialType;
        var selectedTransitions = ImmutableArray.CreateBuilder<TimeZoneTransition>();
        long previous = long.MinValue;
        for (var index = 0; index < transitions.Length; index++)
        {
            var typeIndex = typeIndices[index];
            if (typeIndex >= types.Length)
            {
                throw new FormatException("TZif transition references an invalid local-time type.");
            }
            var unixSeconds = transitions[index];
            if (unixSeconds <= previous)
            {
                throw new FormatException("TZif transitions are not strictly ordered.");
            }
            previous = unixSeconds;
            if (unixSeconds < MinimumUnixSeconds)
            {
                selectedType = typeIndex;
                continue;
            }
            if (unixSeconds <= MaximumUnixSeconds)
            {
                var type = types[typeIndex];
                selectedTransitions.Add(new(
                    unixSeconds,
                    type.OffsetSeconds,
                    type.Daylight));
            }
        }
        var footerOffset = checked(blockOffset + blockLength);
        var footer = ReadFooter(contents, footerOffset);
        if (footer.Length != 0)
        {
            selectedTransitions.AddRange(_futureRules.Expand(
                footer,
                selectedTransitions.Count == 0
                    ? MinimumUnixSeconds - 1
                    : selectedTransitions[^1].UnixSeconds));
        }
        var initial = types[selectedType];
        return new TimeZoneDefinition(
            name,
            initial.OffsetSeconds,
            initial.Daylight,
            selectedTransitions.ToImmutable());
    }

    private static TzifHeader ReadHeader(byte[] contents, int offset)
    {
        Require(contents, offset, HeaderLength);
        if (contents[offset] != 'T' || contents[offset + 1] != 'Z' ||
            contents[offset + 2] != 'i' || contents[offset + 3] != 'f')
        {
            throw new FormatException("Timezone source is not a TZif file.");
        }
        var version = (char)contents[offset + 4];
        if (version is not ('\0' or '2' or '3' or '4'))
        {
            throw new FormatException("TZif version is unsupported.");
        }
        offset += 20;
        var utcIndicatorCount = ReadCount(contents, ref offset);
        var standardIndicatorCount = ReadCount(contents, ref offset);
        var leapCount = ReadCount(contents, ref offset);
        var transitionCount = ReadCount(contents, ref offset);
        var typeCount = ReadCount(contents, ref offset);
        var abbreviationCount = ReadCount(contents, ref offset);
        if (typeCount == 0 || typeCount > 256)
        {
            throw new FormatException("TZif local-time type count is invalid.");
        }
        return new TzifHeader(
            version,
            utcIndicatorCount,
            standardIndicatorCount,
            leapCount,
            transitionCount,
            typeCount,
            abbreviationCount);
    }

    private static int BlockLength(TzifHeader header, int timeSize) => checked(
        header.TransitionCount * timeSize +
        header.TransitionCount +
        header.TypeCount * 6 +
        header.AbbreviationCount +
        header.LeapCount * (timeSize + sizeof(int)) +
        header.StandardIndicatorCount +
        header.UtcIndicatorCount);

    private static string ReadFooter(byte[] contents, int offset)
    {
        if (offset == contents.Length)
        {
            return string.Empty;
        }
        if (contents[offset++] != '\n')
        {
            throw new FormatException("TZif footer is malformed.");
        }
        var start = offset;
        while (offset < contents.Length && contents[offset] != '\n')
        {
            offset++;
        }
        if (offset == contents.Length || offset + 1 != contents.Length)
        {
            throw new FormatException("TZif footer is malformed.");
        }
        return Encoding.ASCII.GetString(contents, start, offset - start);
    }

    private static int ReadCount(byte[] contents, ref int offset)
    {
        var value = ReadInt32(contents, ref offset);
        if (value < 0)
        {
            throw new FormatException("TZif count is negative.");
        }
        return value;
    }

    private static byte ReadByte(byte[] contents, ref int offset)
    {
        Require(contents, offset, 1);
        return contents[offset++];
    }

    private static int ReadInt32(byte[] contents, ref int offset)
    {
        Require(contents, offset, sizeof(int));
        var value = contents[offset] << 24 |
            contents[offset + 1] << 16 |
            contents[offset + 2] << 8 |
            contents[offset + 3];
        offset += sizeof(int);
        return value;
    }

    private static long ReadInt64(byte[] contents, ref int offset)
    {
        var high = ReadInt32(contents, ref offset);
        var low = unchecked((uint)ReadInt32(contents, ref offset));
        return unchecked((long)((ulong)(uint)high << 32 | low));
    }

    private static void Require(byte[] contents, int offset, int count)
    {
        if (offset > contents.Length - count)
        {
            throw new FormatException("TZif file is truncated.");
        }
    }

    private readonly record struct TzifHeader(
        char Version,
        int UtcIndicatorCount,
        int StandardIndicatorCount,
        int LeapCount,
        int TransitionCount,
        int TypeCount,
        int AbbreviationCount);

    private readonly record struct TzifType(int OffsetSeconds, bool Daylight);
}
