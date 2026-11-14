// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Text.RegularExpressions
{
    /// <summary>
    /// An exception as a result of a parse error in a regular expression, with detailed information in the
    /// <see cref="Error"/> and <see cref="Offset"/> properties.
    /// </summary>
    [Serializable]
    public sealed class RegexParseException : ArgumentException
    {
        /// <summary>Gets the error that happened during parsing.</summary>
        /// <value>The error that occurred during parsing.</value>
        public RegexParseError Error { get; }

        /// <summary>
        /// Gets the zero-based character offset in the regular expression pattern where the parse error
        /// occurs.
        /// </summary>
        /// <value>The offset at which the parse error occurs.</value>
        public int Offset { get; }

        internal RegexParseException(RegexParseError error, int offset, string message) : base(message)
        {
            Error = error;
            Offset = offset;
        }
    }
}
