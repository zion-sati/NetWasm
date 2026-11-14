// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.IO
{
    // HResults used by the desktop implementation. The exception base types
    // are supplied by NetWasm.CoreLib; keeping these values here avoids a
    // dependency on the platform exception/resource tables.
    [Serializable]
    [System.Runtime.CompilerServices.TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public class IOException : SystemException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected IOException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("IOException formatter deserialization is not available on this NetWasm platform.");

        private const int CorEIo = unchecked((int)0x80131620);

        public IOException()
        {
            HResult = CorEIo;
        }

        public IOException(string? message)
            : base(message)
        {
            HResult = CorEIo;
        }

        public IOException(string? message, int hresult)
            : base(message)
        {
            HResult = hresult;
        }

        public IOException(string? message, Exception? innerException)
            : base(message, innerException)
        {
            HResult = CorEIo;
        }
    }

    [Serializable]
    [System.Runtime.CompilerServices.TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public class EndOfStreamException : IOException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected EndOfStreamException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("EndOfStreamException formatter deserialization is not available on this NetWasm platform.");

        private const int CorEEndOfStream = unchecked((int)0x80070026);

        public EndOfStreamException()
            : base("Unable to read beyond the end of the stream.")
        {
            HResult = CorEEndOfStream;
        }

        public EndOfStreamException(string? message)
            : base(message ?? "Unable to read beyond the end of the stream.")
        {
            HResult = CorEEndOfStream;
        }

        public EndOfStreamException(string? message, Exception? innerException)
            : base(message ?? "Unable to read beyond the end of the stream.", innerException)
        {
            HResult = CorEEndOfStream;
        }
    }
}
