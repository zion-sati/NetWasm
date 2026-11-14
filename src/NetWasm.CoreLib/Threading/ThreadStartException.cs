// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Resource messages use invariant upstream text.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace System.Threading
{
    [Serializable]
    [TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public sealed class ThreadStartException : SystemException
    {
        internal ThreadStartException()
            : base("Thread failed to start.")
        {
            HResult = HResults.COR_E_THREADSTART;
        }

        internal ThreadStartException(Exception? reason)
            : base("Thread failed to start.", reason)
        {
            HResult = HResults.COR_E_THREADSTART;
        }

        [Obsolete(Obsoletions.LegacyFormatterImplMessage, DiagnosticId = Obsoletions.LegacyFormatterImplDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        private ThreadStartException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
