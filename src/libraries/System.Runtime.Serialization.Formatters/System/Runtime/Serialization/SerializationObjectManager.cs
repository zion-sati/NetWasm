// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
//
// NetWasm deviation: RegisterObject throws PlatformNotSupportedException before it changes any
// state, because it finds an object's [OnSerializing] and [OnSerialized] methods through
// reflection (SerializationEventsCache). No object can therefore be registered, so the
// seen-object table, the stored context and the OnSerialized handler chain are not ported and
// RaiseOnSerializedEvent has no handlers to invoke.

using System.Diagnostics.CodeAnalysis;

namespace System.Runtime.Serialization
{
    [Obsolete(Obsoletions.LegacyFormatterMessage, DiagnosticId = Obsoletions.LegacyFormatterDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
    public sealed class SerializationObjectManager
    {
        private const string SerializationObjectManagerUnreferencedCodeMessage = "SerializationObjectManager is not trim compatible because the type of objects being managed cannot be statically discovered.";

        public SerializationObjectManager(StreamingContext context)
        {
        }

        [RequiresUnreferencedCode(SerializationObjectManagerUnreferencedCodeMessage)]
        public void RegisterObject(object obj)
        {
            throw new PlatformNotSupportedException(SR.Serialization_CallbackDiscoveryNotSupported);
        }

        public void RaiseOnSerializedEvent()
        {
        }
    }
}
