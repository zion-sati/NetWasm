// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tests.Legality;

// A span parameter the method reassigns, then passes by reference, with a loop that passes another span by reference
// to a method inlined into it: Sdcb.Arithmetic's NumberText.TryNormalize, which trims white space and parentheses
// before parsing. At Oz, which inlines calls in loops and promotes address-taken structs with reference fields, the
// reads after the reassignment saw the parameter's original value, so "  -12  " and "(12)" failed to parse.
public sealed class ReassignedSpanParameterLegalityTests
{
    private const string Source = """
        using System;
        using System.Globalization;
        using System.Text;

        public static class EntryPoint
        {
            private static bool IsWhite(char c) => c == ' ' || (c >= '\t' && c <= '\r');

            private static ReadOnlySpan<char> TrimWhite(ReadOnlySpan<char> s)
            {
                int i = 0;
                while (i < s.Length && IsWhite(s[i]))
                {
                    i++;
                }
                s = s.Slice(i);
                int j = s.Length;
                while (j > 0 && IsWhite(s[j - 1]))
                {
                    j--;
                }
                return s.Slice(0, j);
            }

            private static bool TryStripSign(ref ReadOnlySpan<char> s, NumberFormatInfo nfi, bool leading, out bool negative)
            {
                negative = false;
                string ns = nfi.NegativeSign, ps = nfi.PositiveSign;
                if (leading)
                {
                    if (ns.Length > 0 && s.StartsWith(ns, StringComparison.Ordinal))
                    {
                        negative = true;
                        s = s.Slice(ns.Length);
                        return true;
                    }
                    if (ps.Length > 0 && s.StartsWith(ps, StringComparison.Ordinal))
                    {
                        s = s.Slice(ps.Length);
                        return true;
                    }
                }
                else
                {
                    if (ns.Length > 0 && s.EndsWith(ns, StringComparison.Ordinal))
                    {
                        negative = true;
                        s = s.Slice(0, s.Length - ns.Length);
                        return true;
                    }
                    if (ps.Length > 0 && s.EndsWith(ps, StringComparison.Ordinal))
                    {
                        s = s.Slice(0, s.Length - ps.Length);
                        return true;
                    }
                }
                return false;
            }

            private static string? Normalize(ReadOnlySpan<char> s, bool hex, bool parentheses, NumberFormatInfo nfi)
            {
                s = TrimWhite(s);
                if (hex)
                {
                    return s.ToString();
                }
                bool negative = false;
                if (parentheses && s.Length >= 2 && s[0] == '(' && s[^1] == ')')
                {
                    negative = true;
                    s = s.Slice(1, s.Length - 2);
                }
                else if (TryStripSign(ref s, nfi, leading: false, out bool trailing))
                {
                    negative = trailing;
                }
                var sb = new StringBuilder(s.Length + 1);
                if (negative)
                {
                    sb.Append('-');
                }
                int i = 0;
                while (i < s.Length)
                {
                    char c = s[i];
                    if (char.IsAsciiDigit(c))
                    {
                        sb.Append(c);
                        i++;
                    }
                    else if (c == 'e' || c == 'E')
                    {
                        i++;
                        ReadOnlySpan<char> rest = s.Slice(i);
                        bool exponentNegative = false;
                        if (TryStripSign(ref rest, nfi, leading: true, out bool sign))
                        {
                            exponentNegative = sign;
                        }
                        if (rest.IsEmpty || rest.ContainsAnyExceptInRange('0', '9'))
                        {
                            return null;
                        }
                        sb.Append(exponentNegative ? "e-" : "e");
                        sb.Append(rest);
                        i = s.Length;
                    }
                    else
                    {
                        return null;
                    }
                }
                return sb.ToString();
            }

            private static int Code(string? text) => text is null ? -1 : text.Length * 1000 + (text.Length > 0 ? text[0] : 0);

            public static int Run(int input)
            {
                NumberFormatInfo nfi = NumberFormatInfo.InvariantInfo;
                return input switch
                {
                    0 => Code(Normalize("  7fff  ", hex: true, parentheses: false, nfi)),
                    1 => Code(Normalize("  12-  ", hex: false, parentheses: false, nfi)),
                    2 => Code(Normalize("(12)", hex: false, parentheses: true, nfi)),
                    3 => Code(Normalize(" 25e-3 ", hex: false, parentheses: false, nfi)),
                    4 => Code(Normalize("12", hex: false, parentheses: false, nfi)),
                    _ => Code(Normalize("12-", hex: false, parentheses: false, nfi)),
                };
            }
        }
        """;

    // Each result as desktop .NET 11 gives it: the normalized text's length * 1000 + its first character.
    private static readonly (int Input, int Expected)[] Cases =
    [
        (0, 4055), // "7fff"
        (1, 3045), // "-12"
        (2, 3045), // "-12"
        (3, 5050), // "25e-3"
        (4, 2049), // "12"
        (5, 3045), // "-12"
    ];

    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.Os)]
    [InlineData(FinalWasmOptimization.O3)]
    public void AReassignedSpanParameterKeepsItsNewValueAtEveryLevel(FinalWasmOptimization optimization) =>
        LegalityCases.Run($"ReassignedSpanParameter{optimization}", Source, Cases, optimization);

    // Oz's two settings that together lost the reassignment, at Os.
    [Fact]
    public void AReassignedSpanParameterKeepsItsNewValueWithLoopInliningAndReferencePromotion() =>
        LegalityCases.Run("ReassignedSpanParameterOsLoopsReferences", Source, Cases, FinalWasmOptimization.Os,
            new WasmOptimizationOverrides(
                AddressTakenValuePromotion: ManagedAddressTakenValuePromotion.References,
                ManagedMethodInlining: ManagedMethodInlining.Loops));
}
