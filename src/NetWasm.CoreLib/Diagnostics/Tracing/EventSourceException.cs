// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

// Adapted from the pinned runtime: legacy formatter serialization is outside
// this ordinary exception contract slice. Messages use invariant resources.
namespace System.Diagnostics.Tracing
{
    /// <summary>
    /// Exception that is thrown when an error occurs during EventSource operation.
    /// </summary>
    [Serializable]
    public class EventSourceException : Exception
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected EventSourceException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("EventSourceException formatter deserialization is not available on this NetWasm platform.");

        /// <summary>
        /// Initializes a new instance of the EventSourceException class.
        /// </summary>
        public EventSourceException() :
            base("An error occurred when writing to a listener.")
        { }

        /// <summary>
        /// Initializes a new instance of the EventSourceException class with a specified error message.
        /// </summary>
        public EventSourceException(string? message) : base(message ?? "An error occurred when writing to a listener.") { }

        /// <summary>
        /// Initializes a new instance of the EventSourceException class with a specified error message
        /// and a reference to the inner exception that is the cause of this exception.
        /// </summary>
        public EventSourceException(string? message, Exception? innerException) : base(message ?? "An error occurred when writing to a listener.", innerException) { }


        internal EventSourceException(Exception? innerException) :
            base("An error occurred when writing to a listener.", innerException)
        { }
    }
}
