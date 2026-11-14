// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


// Adapted from the pinned runtime: legacy formatter serialization is outside
// this ordinary exception contract slice. Messages use invariant resources.
namespace System.Reflection
{
    [Serializable]
    [System.Runtime.CompilerServices.TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public class InvalidFilterCriteriaException : ApplicationException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected InvalidFilterCriteriaException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("InvalidFilterCriteriaException formatter deserialization is not available on this NetWasm platform.");

        public InvalidFilterCriteriaException()
            : this("Specified filter criteria was invalid.")
        {
        }

        public InvalidFilterCriteriaException(string? message)
            : this(message, null)
        {
        }

        public InvalidFilterCriteriaException(string? message, Exception? inner)
            : base(message ?? "Specified filter criteria was invalid.", inner)
        {
            HResult = HResults.COR_E_INVALIDFILTERCRITERIA;
        }

    }
}
