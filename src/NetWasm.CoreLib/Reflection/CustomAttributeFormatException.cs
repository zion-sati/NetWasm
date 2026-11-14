// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Reflection
{
    [Serializable]
    [System.Runtime.CompilerServices.TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public class CustomAttributeFormatException : FormatException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected CustomAttributeFormatException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("CustomAttributeFormatException formatter deserialization is not available on this NetWasm platform.");

        private const string DefaultMessage = "Binary format of the specified custom attribute was invalid.";

        public CustomAttributeFormatException()
            : this(DefaultMessage)
        {
        }

        public CustomAttributeFormatException(string? message)
            : this(message, null)
        {
        }

        public CustomAttributeFormatException(string? message, Exception? inner)
            : base(message ?? DefaultMessage, inner)
        {
            HResult = unchecked((int)0x80131605);
        }
    }
}
