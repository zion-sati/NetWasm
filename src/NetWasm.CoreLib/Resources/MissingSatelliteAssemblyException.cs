// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


// Adapted from the pinned runtime: legacy formatter serialization is outside
// this ordinary exception contract slice. Messages use invariant resources.
namespace System.Resources
{
    /// <summary>
    /// The exception that is thrown when the satellite assembly for the resources of the default culture is missing.
    /// </summary>
    [Serializable]
    [System.Runtime.CompilerServices.TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public class MissingSatelliteAssemblyException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected MissingSatelliteAssemblyException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("MissingSatelliteAssemblyException formatter deserialization is not available on this NetWasm platform.");

        private readonly string? _cultureName;

        public MissingSatelliteAssemblyException()
            : base("Resource lookup fell back to the ultimate fallback resources in a satellite assembly, but that satellite either was not found or could not be loaded. Please consider reinstalling or repairing the application.")
        {
            HResult = HResults.COR_E_MISSINGSATELLITEASSEMBLY;
        }

        public MissingSatelliteAssemblyException(string? message)
            : base(message ?? "Resource lookup fell back to the ultimate fallback resources in a satellite assembly, but that satellite either was not found or could not be loaded. Please consider reinstalling or repairing the application.")
        {
            HResult = HResults.COR_E_MISSINGSATELLITEASSEMBLY;
        }

        public MissingSatelliteAssemblyException(string? message, string? cultureName)
            : base(message ?? "Resource lookup fell back to the ultimate fallback resources in a satellite assembly, but that satellite either was not found or could not be loaded. Please consider reinstalling or repairing the application.")
        {
            HResult = HResults.COR_E_MISSINGSATELLITEASSEMBLY;
            _cultureName = cultureName;
        }

        public MissingSatelliteAssemblyException(string? message, Exception? inner)
            : base(message ?? "Resource lookup fell back to the ultimate fallback resources in a satellite assembly, but that satellite either was not found or could not be loaded. Please consider reinstalling or repairing the application.", inner)
        {
            HResult = HResults.COR_E_MISSINGSATELLITEASSEMBLY;
        }


        public string? CultureName => _cultureName;
    }
}
