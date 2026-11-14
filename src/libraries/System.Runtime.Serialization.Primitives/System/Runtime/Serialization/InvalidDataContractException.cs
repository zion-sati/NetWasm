// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace System.Runtime.Serialization
{
    [Serializable]
    [TypeForwardedFrom("System.Runtime.Serialization, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public class InvalidDataContractException : Exception
    {
        public InvalidDataContractException()
            : base()
        {
        }

        public InvalidDataContractException(string? message)
            : base(message)
        {
        }

        public InvalidDataContractException(string? message, Exception? innerException)
            : base(message, innerException)
        {
        }

        [Obsolete(Obsoletions.LegacyFormatterImplMessage, DiagnosticId = Obsoletions.LegacyFormatterImplDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected InvalidDataContractException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
