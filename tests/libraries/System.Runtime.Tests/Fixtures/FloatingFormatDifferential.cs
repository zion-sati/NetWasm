// From the compiler harness's FloatingFormatDifferentialCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Globalization;
namespace FloatFormatFixture;

public static class EntryPoint
{
    private static readonly string?[] Formats =
    {
        null, "R", "G", "G9", "G17", "E", "E10", "F0", "F3", "N2", "P1", "C",
        "0.00###", "#,##0.0#;(#,##0.0#);zero", "0.###E+00", "00000.000",
    };

    // Desktop .NET 11's hashes: doubles per format, floats per format, Halves per format, then the
    // round-trip failures for doubles and floats.
    private static readonly ulong[] Expected =
    {
        0xEDDFD993F7FA9F78, 0xEDDFD993F7FA9F78, 0xEDDFD993F7FA9F78, 0x5F11A549BA89BA26,
        0x8CC241371EBCBE6C, 0x5EFACE3C444AE054, 0x6DFB23BBDD416982, 0x9624F5D60ECD2C14,
        0xF1070249AAA76A5D, 0x01CBC1735227F2CB, 0xEE99EC8B96631596, 0x989933A4AACD38F5,
        0x97CD68671C81CC4D, 0x38A73BCAE8982BFE, 0x7A2627BDE25EEF6D, 0x76546647685C30DA,
        0x20F70B82CF09C38D, 0x20F70B82CF09C38D, 0x20F70B82CF09C38D, 0xE4718817F0EE19CB,
        0xF46E4572003D46DB, 0x9A065CC1DD436722, 0xDC04750A800FBC03, 0x87A6922E566BC3C9,
        0x91B1322172A5CCF5, 0x0FF66540E3E6B0EA, 0xC2DF603CF35B06F5, 0x7C06CBC81E9C96E8,
        0x8386D79D52CF0B61, 0x41045D62AEB44AF7, 0xC74AD43BBCB6843E, 0xA0781B4AD4D2B5D7,
        0x2C0F6275F4E39633, 0x2C0F6275F4E39633, 0x2C0F6275F4E39633, 0x3DF12D02CCEBB2AC,
        0x336D09AFEDBE888F, 0x7D0EB9A71E0D9935, 0x29466C7AC11A992B, 0x91B2F304797889DC,
        0x838DE15225553A9E, 0xDBD171F15FFED2C7, 0xC4EEB2A23BB87F50, 0x672BBEC477DD0681,
        0xB0A024B91633C1E1, 0x63D010E2172EB282, 0x480393D5A32552FD, 0x70AA425589CAB23F,
        0x0000000000000000, 0x0000000000000000,
    };

    private static ulong s_state = 0x9E3779B97F4A7C15;

    private static ulong Next()
    {
        s_state ^= s_state << 13;
        s_state ^= s_state >> 7;
        s_state ^= s_state << 17;
        return s_state;
    }

    private static ulong Mix(ulong hash, string text)
    {
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 1099511628211;
        }

        hash ^= 0xFF;
        hash *= 1099511628211;
        return hash;
    }

    private static double[] Doubles()
    {
        var values = new System.Collections.Generic.List<double>
        {
            0.0, -0.0, 1.0, -1.0, 0.1, 0.2, 0.3, 1.0 / 3, 2.0 / 3, 0.5, 1.5, 2.5, -2.5, 9.5, 99.5, 0.125,
            123456789.0, 1e15, 1e16, 1e17, 9007199254740992.0, 9007199254740993.0, 1e21, 1e22, 1e23,
            1e-5, 1e-6, 1e-7, 1e-300, 1e300, double.MaxValue, double.MinValue, double.Epsilon,
            double.NaN, double.PositiveInfinity, double.NegativeInfinity, 2.2250738585072014e-308,
            4.9406564584124654e-324, 0.30000000000000004, 1.7976931348623157e308, 5e-324, 123.456,
            -0.000123456, 1234567.891, 0.045, 0.055, 1.005, 2.675, 1e-1, 1e-2, 1e-3, 1e-4,
        };
        for (int i = 0; i < 800; i++)
        {
            values.Add(BitConverter.Int64BitsToDouble((long)Next()));
        }

        for (int i = 0; i < 500; i++)
        {
            long mantissa = (long)(Next() % 100000000000UL);
            int scale = (int)(Next() % 40) - 20;
            values.Add(mantissa * Math.Pow(10, scale) * ((Next() & 1) == 0 ? 1 : -1));
        }

        return values.ToArray();
    }

    private static float[] Singles()
    {
        var values = new System.Collections.Generic.List<float>
        {
            0f, -0f, 1f, -1f, 0.1f, 0.2f, 0.3f, 1f / 3, 0.5f, 2.5f, 16777216f, 16777217f, 1e10f, 1e-10f,
            float.MaxValue, float.MinValue, float.Epsilon, float.NaN, float.PositiveInfinity,
            float.NegativeInfinity, 1.17549435e-38f, 3.4028235e38f, 123.456f, 0.045f, 1.005f,
        };
        for (int i = 0; i < 800; i++)
        {
            values.Add(BitConverter.Int32BitsToSingle((int)Next()));
        }

        for (int i = 0; i < 500; i++)
        {
            int mantissa = (int)(Next() % 10000000UL);
            int scale = (int)(Next() % 20) - 10;
            values.Add((float)(mantissa * Math.Pow(10, scale)));
        }

        return values.ToArray();
    }

    private static Half[] Halves()
    {
        var values = new Half[300];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = BitConverter.UInt16BitsToHalf((ushort)Next());
        }

        values[0] = Half.MaxValue;
        values[1] = Half.Epsilon;
        values[2] = Half.NaN;
        values[3] = (Half)0.1f;
        return values;
    }

    public static ulong[] Hashes()
    {
        CultureInfo invariant = CultureInfo.InvariantCulture;
        double[] doubles = Doubles();
        float[] singles = Singles();
        Half[] halves = Halves();
        var hashes = new ulong[Formats.Length * 3 + 2];
        for (int f = 0; f < Formats.Length; f++)
        {
            ulong d = 14695981039346656037, s = 14695981039346656037, h = 14695981039346656037;
            foreach (double value in doubles)
            {
                d = Mix(d, value.ToString(Formats[f], invariant));
            }

            foreach (float value in singles)
            {
                s = Mix(s, value.ToString(Formats[f], invariant));
            }

            foreach (Half value in halves)
            {
                h = Mix(h, value.ToString(Formats[f], invariant));
            }

            hashes[f] = d;
            hashes[Formats.Length + f] = s;
            hashes[2 * Formats.Length + f] = h;
        }

        ulong doubleFailures = 0, singleFailures = 0;
        foreach (double value in doubles)
        {
            double back = double.Parse(value.ToString("R", invariant), invariant);
            if (BitConverter.DoubleToInt64Bits(back) != BitConverter.DoubleToInt64Bits(value) && !(double.IsNaN(back) && double.IsNaN(value)))
            {
                doubleFailures++;
            }
        }

        foreach (float value in singles)
        {
            float back = float.Parse(value.ToString(invariant), invariant);
            if (BitConverter.SingleToInt32Bits(back) != BitConverter.SingleToInt32Bits(value) && !(float.IsNaN(back) && float.IsNaN(value)))
            {
                singleFailures++;
            }
        }

        hashes[3 * Formats.Length] = doubleFailures;
        hashes[3 * Formats.Length + 1] = singleFailures;
        return hashes;
    }

    public static int Run(int input)
    {
        ulong[] actual = Hashes();
#if DESKTOP_ORACLE
        Console.WriteLine(string.Join(", ", Array.ConvertAll(actual, hash => "0x" + hash.ToString("X16"))));
#endif
        for (int i = 0; i < actual.Length; i++)
        {
            if (actual[i] != Expected[i])
            {
                return 100 + i;
            }
        }

        return input + 1;
    }
}
