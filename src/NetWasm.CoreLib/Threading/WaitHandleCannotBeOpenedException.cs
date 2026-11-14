// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Resource messages use invariant upstream text.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace System.Threading
{
    [Serializable]
    [TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public class WaitHandleCannotBeOpenedException : ApplicationException
    {
        public WaitHandleCannotBeOpenedException() : base("No handle of the given name exists.")
        {
            HResult = HResults.COR_E_WAITHANDLECANNOTBEOPENED;
        }

        public WaitHandleCannotBeOpenedException(string? message) : base(message ?? "No handle of the given name exists.")
        {
            HResult = HResults.COR_E_WAITHANDLECANNOTBEOPENED;
        }

        public WaitHandleCannotBeOpenedException(string? message, Exception? innerException) : base(message ?? "No handle of the given name exists.", innerException)
        {
            HResult = HResults.COR_E_WAITHANDLECANNOTBEOPENED;
        }

        [Obsolete(Obsoletions.LegacyFormatterImplMessage, DiagnosticId = Obsoletions.LegacyFormatterImplDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected WaitHandleCannotBeOpenedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
