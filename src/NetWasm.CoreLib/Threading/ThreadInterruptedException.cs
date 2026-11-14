// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Resource messages use invariant upstream text.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace System.Threading
{
    /// <summary>
    /// An exception class to indicate that the thread was interrupted from a waiting state.
    /// </summary>
    [Serializable]
    [TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public class ThreadInterruptedException : SystemException
    {
        public ThreadInterruptedException() : base(GetDefaultMessage())
        {
            HResult = HResults.COR_E_THREADINTERRUPTED;
        }

        public ThreadInterruptedException(string? message)
            : base(message ?? GetDefaultMessage())
        {
            HResult = HResults.COR_E_THREADINTERRUPTED;
        }

        public ThreadInterruptedException(string? message, Exception? innerException)
            : base(message ?? GetDefaultMessage(), innerException)
        {
            HResult = HResults.COR_E_THREADINTERRUPTED;
        }

        private static string GetDefaultMessage()
#if CORECLR
            => GetMessageFromNativeResources(ExceptionMessageKind.ThreadInterrupted);
#else
            => "Thread was interrupted from a waiting state.";
#endif

        [Obsolete(Obsoletions.LegacyFormatterImplMessage, DiagnosticId = Obsoletions.LegacyFormatterImplDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected ThreadInterruptedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
