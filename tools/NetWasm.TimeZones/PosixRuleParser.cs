namespace NetWasm.TimeZones;

internal sealed class PosixRuleParser : IPosixRuleParser
{
    public PosixFutureRule? Parse(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        var offset = 0;
        ReadName(value, ref offset);
        var standardOffset = -ReadOffset(value, ref offset);
        if (offset == value.Length)
        {
            return null;
        }
        ReadName(value, ref offset);
        var daylightOffset = offset < value.Length && IsOffsetStart(value[offset])
            ? -ReadOffset(value, ref offset)
            : checked(standardOffset + 60 * 60);
        Require(value, ref offset, ',');
        var start = ReadTransition(value, ref offset);
        Require(value, ref offset, ',');
        var end = ReadTransition(value, ref offset);
        if (offset != value.Length)
        {
            throw new FormatException("POSIX timezone rule contains trailing data.");
        }
        return new PosixFutureRule(
            standardOffset,
            daylightOffset,
            start,
            end);
    }

    private static void ReadName(string value, ref int offset)
    {
        if (value[offset] == '<')
        {
            var start = ++offset;
            while (offset < value.Length && value[offset] != '>')
            {
                offset++;
            }
            if (offset == value.Length || offset == start)
            {
                throw new FormatException("POSIX timezone name is invalid.");
            }
            offset++;
            return;
        }
        var count = 0;
        while (offset < value.Length && IsAsciiLetter(value[offset]))
        {
            offset++;
            count++;
        }
        if (count < 3)
        {
            throw new FormatException("POSIX timezone name is invalid.");
        }
    }

    private static PosixTransitionRule ReadTransition(string value, ref int offset)
    {
        var date = ReadDate(value, ref offset);
        var seconds = 2 * 60 * 60;
        var basis = PosixTimeBasis.Wall;
        if (offset < value.Length && value[offset] == '/')
        {
            offset++;
            seconds = ReadSignedTime(value, ref offset);
            if (offset < value.Length)
            {
                basis = value[offset] switch
                {
                    's' => PosixTimeBasis.Standard,
                    'u' or 'g' or 'z' => PosixTimeBasis.Utc,
                    'w' => PosixTimeBasis.Wall,
                    _ => basis,
                };
                if (value[offset] is 's' or 'u' or 'g' or 'z' or 'w')
                {
                    offset++;
                }
            }
        }
        return new PosixTransitionRule(date, seconds, basis);
    }

    private static PosixDateRule ReadDate(string value, ref int offset)
    {
        if (offset < value.Length && value[offset] == 'M')
        {
            offset++;
            var month = ReadNumber(value, ref offset, 1, 12);
            Require(value, ref offset, '.');
            var week = ReadNumber(value, ref offset, 1, 5);
            Require(value, ref offset, '.');
            var day = ReadNumber(value, ref offset, 0, 6);
            return new PosixDateRule(
                PosixDateRuleKind.MonthWeekDay,
                month,
                week,
                day);
        }
        if (offset < value.Length && value[offset] == 'J')
        {
            offset++;
            return new PosixDateRule(
                PosixDateRuleKind.JulianWithoutLeapDay,
                ReadNumber(value, ref offset, 1, 365),
                0,
                0);
        }
        return new PosixDateRule(
            PosixDateRuleKind.ZeroBasedDayOfYear,
            ReadNumber(value, ref offset, 0, 365),
            0,
            0);
    }

    private static int ReadOffset(string value, ref int offset) =>
        ReadSignedClock(value, ref offset, 24);

    private static int ReadSignedTime(string value, ref int offset) =>
        ReadSignedClock(value, ref offset, 167);

    private static int ReadSignedClock(string value, ref int offset, int maximumHour)
    {
        var sign = 1;
        if (offset < value.Length && value[offset] is '+' or '-')
        {
            sign = value[offset++] == '-' ? -1 : 1;
        }
        var hour = ReadNumber(value, ref offset, 0, maximumHour);
        var minute = 0;
        var second = 0;
        if (offset < value.Length && value[offset] == ':')
        {
            offset++;
            minute = ReadNumber(value, ref offset, 0, 59);
            if (offset < value.Length && value[offset] == ':')
            {
                offset++;
                second = ReadNumber(value, ref offset, 0, 59);
            }
        }
        return checked(sign * (hour * 60 * 60 + minute * 60 + second));
    }

    private static int ReadNumber(
        string value,
        ref int offset,
        int minimum,
        int maximum)
    {
        if (offset == value.Length || !char.IsAsciiDigit(value[offset]))
        {
            throw new FormatException("POSIX timezone rule expected a number.");
        }
        var result = 0;
        try
        {
            do
            {
                result = checked(result * 10 + value[offset++] - '0');
            }
            while (offset < value.Length && char.IsAsciiDigit(value[offset]));
        }
        catch (OverflowException exception)
        {
            throw new FormatException(
                "POSIX timezone number is outside its valid range.",
                exception);
        }
        if (result < minimum || result > maximum)
        {
            throw new FormatException("POSIX timezone number is outside its valid range.");
        }
        return result;
    }

    private static void Require(string value, ref int offset, char expected)
    {
        if (offset == value.Length || value[offset] != expected)
        {
            throw new FormatException("POSIX timezone rule is malformed.");
        }
        offset++;
    }

    private static bool IsOffsetStart(char value) =>
        char.IsAsciiDigit(value) || value is '+' or '-';

    private static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}
