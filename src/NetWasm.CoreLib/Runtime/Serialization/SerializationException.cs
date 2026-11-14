// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


// Adapted from the pinned runtime: legacy formatter serialization is outside
// this ordinary exception contract slice. Messages use invariant resources.
namespace System.Runtime.Serialization
{
    [Serializable]
    [System.Runtime.CompilerServices.TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public class SerializationException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected SerializationException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("SerializationException formatter deserialization is not available on this NetWasm platform.");

        /// <summary>
        /// Creates a new SerializationException with its message
        /// string set to a default message.
        /// </summary>
        public SerializationException()
            : base("Serialization error.")
        {
            HResult = HResults.COR_E_SERIALIZATION;
        }

        public SerializationException(string? message)
            : base(message ?? "Serialization error.")
        {
            HResult = HResults.COR_E_SERIALIZATION;
        }

        public SerializationException(string? message, Exception? innerException)
            : base(message ?? "Serialization error.", innerException)
        {
            HResult = HResults.COR_E_SERIALIZATION;
        }

    }
}
