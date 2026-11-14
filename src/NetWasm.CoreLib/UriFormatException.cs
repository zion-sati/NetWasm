// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    /// <summary>
    /// An exception class used when an invalid Uniform Resource Identifier is detected.
    /// </summary>
    [Serializable]
    [System.Runtime.CompilerServices.TypeForwardedFrom("System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public class UriFormatException : FormatException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected UriFormatException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("UriFormatException formatter deserialization is not available on this NetWasm platform.");

        public UriFormatException() : base()
        {
        }

        public UriFormatException(string? textString) : base(textString)
        {
        }

        public UriFormatException(string? textString, Exception? e) : base(textString, e)
        {
        }
    }
}
