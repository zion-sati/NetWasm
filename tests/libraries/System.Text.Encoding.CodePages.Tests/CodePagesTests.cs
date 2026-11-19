// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Text.Encoding.CodePages.Tests;

// The code-page encodings as desktop .NET 11 gives them, with their tables read from the library's manifest resource:
// single-byte, double-byte (Shift-JIS, GBK, Big5, EUC-KR), GB18030's four-byte sequences, ISO-2022-JP's escapes and
// EUC-JP, by code page and by name, with replacement for characters a code page lacks.
public sealed class CodePagesTests
{
    private const string Sample = "Grüße, ¥€ 東京 ﾃｽﾄ 한국 中文 𠀋";

    static CodePagesTests() => global::System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    // Desktop .NET 11's bytes for the sample, in Base64.
    [Test]
    [Arguments(1252, "R3L832UsIKWAID8/ID8/PyA/PyA/PyA/Pw==")]
    [Arguments(932, "R3J1c2UsIFw/IJOMi54gw73EID8/IJKGlbYgPz8=")]
    [Arguments(936, "R3KouT9lLCCjpIAglny+qSA/Pz8gPz8g1tDOxCA/Pw==")]
    [Arguments(950, "R3J1c2UsIKJEo+EgqkaoyiA/Pz8gPz8gpKSk5SA/Pw==")]
    [Arguments(949, "R3J1qaxlLCChzaLmINTUzMggPz8/IMfRsbkg8enZ/iA/Pw==")]
    [Arguments(54936, "R3KouYEwiThlLCCBMIQ2ouMglny+qSCEMZkxhDGYNYQxmTIggzaEM4I39DAg1tDOxCCVMoM3")]
    [Arguments(50220, "R3J1c2UsIFw/IBskQkVsNX4bKEIgGyRCJUYlOSVIGyhCID8/IBskQkNmSjgbKEIgPz8=")]
    [Arguments(51932, "R3J1c2UsIFw/IMXstf4gjsOOvY7EID8/IMPmyrggPz8=")]
    public async Task EncodesAsDesktop(int codePage, string expected)
    {
        var encoding = global::System.Text.Encoding.GetEncoding(codePage);

        await Assert.That(Convert.ToBase64String(encoding.GetBytes(Sample))).IsEqualTo(expected);
    }

    [Test]
    [Arguments(932)]
    [Arguments(936)]
    [Arguments(950)]
    [Arguments(949)]
    [Arguments(54936)]
    [Arguments(50220)]
    public async Task RoundTripsWhatTheCodePageHas(int codePage)
    {
        var encoding = global::System.Text.Encoding.GetEncoding(codePage);
        var text = string.Concat(Sample.Where(character => encoding.GetString(encoding.GetBytes(character.ToString())) == character.ToString()));

        await Assert.That(encoding.GetString(encoding.GetBytes(text))).IsEqualTo(text);
    }

    [Test]
    public async Task FindsEncodingsByNameAndListsThem()
    {
        await Assert.That(global::System.Text.Encoding.GetEncoding("shift_jis").CodePage).IsEqualTo(932);
        await Assert.That(global::System.Text.Encoding.GetEncoding("windows-1251").CodePage).IsEqualTo(1251);
        await Assert.That(global::System.Text.Encoding.GetEncoding("gb2312").CodePage).IsEqualTo(936);
        await Assert.That(CodePagesEncodingProvider.Instance.GetEncodings().Count()).IsEqualTo(109);
        await Assert.That(CodePagesEncodingProvider.Instance.GetEncoding(12345)).IsNull();
    }

    [Test]
    public async Task DecodesWindows1251AndDecodesInvalidBytesWithReplacement()
    {
        var cyrillic = global::System.Text.Encoding.GetEncoding(1251);
        await Assert.That(cyrillic.GetString([0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2])).IsEqualTo("Привет");
        var shiftJis = global::System.Text.Encoding.GetEncoding(932);
        await Assert.That(shiftJis.GetString([0x82, 0xA0, 0x81])).IsEqualTo("\u3042\u30FB");
    }
}
