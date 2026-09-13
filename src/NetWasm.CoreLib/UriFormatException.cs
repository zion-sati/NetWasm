// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    /// <summary>Thrown when a URI cannot be parsed under the requested kind.</summary>
    public class UriFormatException : FormatException
    {
        public UriFormatException()
        {
        }

        public UriFormatException(string? textString) : base(textString)
        {
        }

        public UriFormatException(string? textString, Exception? innerException) : base(textString, innerException)
        {
        }
    }
}
