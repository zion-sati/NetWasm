// From the compiler harness's NormalizationContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Text;
namespace NormalizationFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var caught = 0;
        try { StringNormalizationExtensions.Normalize(null); } catch (ArgumentNullException e) { if (e.ParamName == "strInput") caught++; }
        try { StringNormalizationExtensions.Normalize(null, NormalizationForm.FormD); } catch (ArgumentNullException e) { if (e.ParamName == "strInput") caught++; }
        try { StringNormalizationExtensions.IsNormalized(null); } catch (ArgumentNullException e) { if (e.ParamName == "strInput") caught++; }
        try { StringNormalizationExtensions.IsNormalized(null, NormalizationForm.FormKC); } catch (ArgumentNullException e) { if (e.ParamName == "strInput") caught++; }
        if (caught != 4) return 1;

        var ascii = "abc";
        var decomposed = "e\u0301";
        if (!ReferenceEquals(ascii.Normalize(), ascii) || !ascii.IsNormalized()) return 2;
        if (!ReferenceEquals(StringNormalizationExtensions.Normalize(ascii), ascii) || !StringNormalizationExtensions.IsNormalized(ascii)) return 3;
        foreach (var form in new[] { NormalizationForm.FormC, NormalizationForm.FormD, NormalizationForm.FormKC, NormalizationForm.FormKD })
        {
            if (!ReferenceEquals(decomposed.Normalize(form), decomposed) || !decomposed.IsNormalized(form)) return 4;
            if (!ReferenceEquals(StringNormalizationExtensions.Normalize(decomposed, form), decomposed) || !StringNormalizationExtensions.IsNormalized(decomposed, form)) return 5;
        }

        foreach (var invalid in new[] { (NormalizationForm)0, (NormalizationForm)3, (NormalizationForm)7 })
        {
            try { ascii.Normalize(invalid); return 6; } catch (ArgumentException e) when (e.ParamName == "normalizationForm") { }
            try { decomposed.IsNormalized(invalid); return 7; } catch (ArgumentException e) when (e.ParamName == "normalizationForm") { }
        }
        return input + 1;
    }
}
