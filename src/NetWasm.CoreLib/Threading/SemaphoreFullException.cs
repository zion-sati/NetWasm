// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace System.Threading
{
    [Serializable]
    [TypeForwardedFrom("System, Version=2.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public class SemaphoreFullException : SystemException
    {
        public SemaphoreFullException() : base("Adding the specified count to the semaphore would cause it to exceed its maximum count.")
        {
        }

        public SemaphoreFullException(string? message) : base(message ?? "Adding the specified count to the semaphore would cause it to exceed its maximum count.")
        {
        }

        public SemaphoreFullException(string? message, Exception? innerException) : base(message ?? "Adding the specified count to the semaphore would cause it to exceed its maximum count.", innerException)
        {
        }

        [Obsolete(Obsoletions.LegacyFormatterImplMessage, DiagnosticId = Obsoletions.LegacyFormatterImplDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected SemaphoreFullException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
