// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: NetWasm always runs with invariant globalization (owner decision
// 2026-10-21), so upstream's GlobalizationMode.Invariant paths are the only ones: every string
// counts as normalized and is returned unchanged. The ICU and NLS paths, the browser check for
// compatibility forms (which applies only outside invariant mode) and the span-only members are
// not ported.

using System.Text;

namespace System.Globalization
{
    internal static partial class Normalization
    {
        internal static bool IsNormalized(ReadOnlySpan<char> source, NormalizationForm normalizationForm = NormalizationForm.FormC)
        {
            CheckNormalizationForm(normalizationForm);

            // In Invariant mode we assume all characters are normalized because we don't support any linguistic operations on strings.
            return true;
        }

        internal static string Normalize(string strInput, NormalizationForm normalizationForm)
        {
            CheckNormalizationForm(normalizationForm);

            // In Invariant mode we assume all characters are normalized because we don't support any linguistic operations on strings.
            return strInput;
        }

        private static void CheckNormalizationForm(NormalizationForm normalizationForm)
        {
            if (normalizationForm != NormalizationForm.FormC &&
                normalizationForm != NormalizationForm.FormD &&
                normalizationForm != NormalizationForm.FormKC &&
                normalizationForm != NormalizationForm.FormKD)
            {
                throw new ArgumentException(SR.Argument_InvalidNormalizationForm, nameof(normalizationForm));
            }
        }
    }
}
