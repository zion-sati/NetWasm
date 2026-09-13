// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.UriParsing;

internal sealed class UriIdnNormalizer : IUriIdnNormalizer
{
    private const int Base = 36;
    private const int TMin = 1;
    private const int TMax = 26;
    private const int Skew = 38;
    private const int Damp = 700;
    private const int InitialBias = 72;
    private const int InitialN = 128;

    public string Normalize(string host)
    {
        if (host.Length == 0)
        {
            return string.Empty;
        }
        if (host[0] == '[' && host[host.Length - 1] == ']')
        {
            return host.Substring(1, host.Length - 2);
        }

        var result = string.Empty;
        var start = 0;
        while (start <= host.Length)
        {
            var separator = host.IndexOf('.', start);
            var end = separator < 0 ? host.Length : separator;
            if (result.Length != 0)
            {
                result += ".";
            }
            result += NormalizeLabel(host.Substring(start, end - start));
            if (separator < 0)
            {
                break;
            }
            start = separator + 1;
        }
        return result;
    }

    private static string NormalizeLabel(string label)
    {
        var lower = label.ToLowerInvariant();
        var hasNonAscii = false;
        for (var index = 0; index < lower.Length; index++)
        {
            if (lower[index] > 0x7f)
            {
                hasNonAscii = true;
                break;
            }
        }
        return hasNonAscii ? "xn--" + Encode(lower) : lower;
    }

    private static string Encode(string label)
    {
        var codePoints = ToCodePoints(label);
        var output = string.Empty;
        var basicCount = 0;
        for (var index = 0; index < codePoints.Length; index++)
        {
            if (codePoints[index] < 0x80)
            {
                output += (char)codePoints[index];
                basicCount++;
            }
        }

        var handled = basicCount;
        if (basicCount > 0)
        {
            output += "-";
        }

        var n = InitialN;
        var delta = 0;
        var bias = InitialBias;
        while (handled < codePoints.Length)
        {
            var next = int.MaxValue;
            for (var index = 0; index < codePoints.Length; index++)
            {
                if (codePoints[index] >= n && codePoints[index] < next)
                {
                    next = codePoints[index];
                }
            }
            delta += (next - n) * (handled + 1);
            n = next;
            for (var index = 0; index < codePoints.Length; index++)
            {
                if (codePoints[index] < n)
                {
                    delta++;
                }
                if (codePoints[index] != n)
                {
                    continue;
                }

                var value = delta;
                for (var divisor = Base; ; divisor += Base)
                {
                    var threshold = divisor <= bias
                        ? TMin
                        : divisor >= bias + TMax
                            ? TMax
                            : divisor - bias;
                    if (value < threshold)
                    {
                        break;
                    }
                    output += Digit(threshold + (value - threshold) % (Base - threshold));
                    value = (value - threshold) / (Base - threshold);
                }
                output += Digit(value);
                bias = Adapt(delta, handled + 1, handled == basicCount);
                delta = 0;
                handled++;
            }
            delta++;
            n++;
        }
        return output;
    }

    private static int[] ToCodePoints(string value)
    {
        var result = new int[value.Length];
        var count = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (current is >= '\uD800' and <= '\uDBFF' &&
                index + 1 < value.Length &&
                value[index + 1] is >= '\uDC00' and <= '\uDFFF')
            {
                result[count++] = 0x10000 + ((current - '\uD800') << 10) +
                    value[++index] - '\uDC00';
            }
            else
            {
                result[count++] = current;
            }
        }
        var trimmed = new int[count];
        for (var index = 0; index < count; index++)
        {
            trimmed[index] = result[index];
        }
        return trimmed;
    }

    private static int Adapt(int delta, int points, bool first)
    {
        delta = first ? delta / Damp : delta / 2;
        delta += delta / points;
        var k = 0;
        while (delta > ((Base - TMin) * TMax) / 2)
        {
            delta /= Base - TMin;
            k += Base;
        }
        return k + ((Base - TMin + 1) * delta) / (delta + Skew);
    }

    private static char Digit(int value) =>
        (char)(value < 26 ? 'a' + value : '0' + value - 26);
}
