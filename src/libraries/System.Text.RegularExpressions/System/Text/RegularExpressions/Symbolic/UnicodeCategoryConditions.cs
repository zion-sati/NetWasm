// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;

namespace System.Text.RegularExpressions.Symbolic
{
    /// <summary>Utility class providing singleton <see cref="BDD"/>s for evaluating whether a character is part of a particular Unicode category.</summary>
    internal static class UnicodeCategoryConditions
    {
        /// <summary>The number of values in <see cref="UnicodeCategory"/>.</summary>
        private const int UnicodeCategoryValueCount = 30;

        /// <summary>Array containing lazily-initialized BDDs per defined UnicodeCategory value.</summary>
        private static readonly BDD?[] s_categories = new BDD[UnicodeCategoryValueCount];
        /// <summary>Lazily-initialized BDD for \w.</summary>
        private static BDD? s_wordLetter;
        /// <summary>Lazily-initialized BDD for \b.</summary>
        private static BDD? s_wordLetterForAnchors;

        /// <summary>Gets a <see cref="BDD"/> that represents the specified <see cref="UnicodeCategory"/>.</summary>
        public static BDD GetCategory(UnicodeCategory category) =>
            s_categories[(int)category] ??= BDD.Deserialize(UnicodeCategoryRanges.GetSerializedCategory(category));

        /// <summary>Gets a <see cref="BDD"/> that represents the \s character class.</summary>
        public static BDD WhiteSpace =>
            field ??= BDD.Deserialize(UnicodeCategoryRanges.SerializedWhitespaceBDD);

        /// <summary>Gets a <see cref="BDD"/> that represents the \w character class.</summary>
        /// <remarks>\w is the union of the 8 categories: 0,1,2,3,4,5,8,18</remarks>
        public static BDD WordLetter(CharSetSolver solver) =>
            s_wordLetter ??= solver.Or(
                [
                    GetCategory(UnicodeCategory.UppercaseLetter),
                    GetCategory(UnicodeCategory.LowercaseLetter),
                    GetCategory(UnicodeCategory.TitlecaseLetter),
                    GetCategory(UnicodeCategory.ModifierLetter),
                    GetCategory(UnicodeCategory.OtherLetter),
                    GetCategory(UnicodeCategory.NonSpacingMark),
                    GetCategory(UnicodeCategory.DecimalDigitNumber),
                    GetCategory(UnicodeCategory.ConnectorPunctuation),
                ]);

        /// <summary>
        /// Gets a <see cref="BDD"/> that represents <see cref="WordLetter"/> together with the characters
        /// \u200C (zero width non joiner) and \u200D (zero width joiner) that are treated as if they were
        /// word characters in the context of the anchors \b and \B.
        /// </summary>
        public static BDD WordLetterForAnchors(CharSetSolver solver) =>
            s_wordLetterForAnchors ??= solver.Or(WordLetter(solver), solver.CreateBDDFromRange('\u200C', '\u200D'));
    }
}
