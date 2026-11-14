// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using System.ComponentModel;

// Adapted from the pinned runtime: legacy formatter serialization is outside
// this ordinary exception contract slice. Messages use invariant resources.
namespace System.Resources
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Serializable]
    [System.Runtime.CompilerServices.TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public class MissingManifestResourceException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected MissingManifestResourceException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("MissingManifestResourceException formatter deserialization is not available on this NetWasm platform.");

        public MissingManifestResourceException()
            : base("Unable to find manifest resource.")
        {
            HResult = HResults.COR_E_MISSINGMANIFESTRESOURCE;
        }

        public MissingManifestResourceException(string? message)
            : base(message ?? "Unable to find manifest resource.")
        {
            HResult = HResults.COR_E_MISSINGMANIFESTRESOURCE;
        }

        public MissingManifestResourceException(string? message, Exception? inner)
            : base(message ?? "Unable to find manifest resource.", inner)
        {
            HResult = HResults.COR_E_MISSINGMANIFESTRESOURCE;
        }

    }
}
