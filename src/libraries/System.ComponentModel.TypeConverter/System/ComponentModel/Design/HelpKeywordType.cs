// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.ComponentModel.Design
{
    /// <summary>
    /// Specifies identifiers that can be used to indicate the type of a help keyword.
    /// </summary>
    public enum HelpKeywordType
    {
        /// <summary>
        /// Indicates the keyword is a word F1 was pressed to request help regarding.
        /// </summary>
        F1Keyword,
        /// <summary>
        /// Indicates the keyword is a general keyword.
        /// </summary>
        GeneralKeyword,
        /// <summary>
        /// Indicates the keyword is a filter keyword.
        /// </summary>
        FilterKeyword
    }
}
