// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
//
// NetWasm deviation: the object graph engine is not ported. The four RegisterObject overloads,
// the RecordFixup, RecordDelayedFixup and RecordArrayElementFixup staging calls, DoFixups and
// RaiseOnDeserializingEvent throw PlatformNotSupportedException before they change any state:
// completing a registered graph calls serialization constructors, writes fields through
// FieldInfo and finds [OnDeserializing]/[OnDeserialized] methods through reflection. With
// registration rejected the holder table is always empty, so GetObject validates its argument
// and finds nothing, and RaiseDeserializationEvent has no handlers to invoke. The members stay
// virtual so subclasses can override them.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace System.Runtime.Serialization
{
    [Obsolete(Obsoletions.LegacyFormatterMessage, DiagnosticId = Obsoletions.LegacyFormatterDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
    public class ObjectManager
    {
        private const string ObjectManagerUnreferencedCodeMessage = "ObjectManager is not trim compatible because the type of objects being managed cannot be statically discovered.";

        public ObjectManager(ISurrogateSelector? selector, StreamingContext context)
        {
        }

        public virtual object? GetObject(long objectID)
        {
            if (objectID <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(objectID), SR.ArgumentOutOfRange_ObjectID);
            }

            return null;
        }

        [RequiresUnreferencedCode(ObjectManagerUnreferencedCodeMessage)]
        public virtual void RegisterObject(object obj, long objectID)
        {
            throw new PlatformNotSupportedException(SR.Serialization_ObjectGraphNotSupported);
        }

        [RequiresUnreferencedCode(ObjectManagerUnreferencedCodeMessage)]
        public void RegisterObject(object obj, long objectID, SerializationInfo info)
        {
            throw new PlatformNotSupportedException(SR.Serialization_ObjectGraphNotSupported);
        }

        [RequiresUnreferencedCode(ObjectManagerUnreferencedCodeMessage)]
        public void RegisterObject(object obj, long objectID, SerializationInfo? info, long idOfContainingObj, MemberInfo? member)
        {
            throw new PlatformNotSupportedException(SR.Serialization_ObjectGraphNotSupported);
        }

        [RequiresUnreferencedCode(ObjectManagerUnreferencedCodeMessage)]
        public void RegisterObject(object obj, long objectID, SerializationInfo? info, long idOfContainingObj, MemberInfo? member, int[]? arrayIndex)
        {
            throw new PlatformNotSupportedException(SR.Serialization_ObjectGraphNotSupported);
        }

        [RequiresUnreferencedCode(ObjectManagerUnreferencedCodeMessage)]
        public virtual void DoFixups()
        {
            throw new PlatformNotSupportedException(SR.Serialization_ObjectGraphNotSupported);
        }

        public virtual void RecordFixup(long objectToBeFixed, MemberInfo member, long objectRequired)
        {
            throw new PlatformNotSupportedException(SR.Serialization_ObjectGraphNotSupported);
        }

        public virtual void RecordDelayedFixup(long objectToBeFixed, string memberName, long objectRequired)
        {
            throw new PlatformNotSupportedException(SR.Serialization_ObjectGraphNotSupported);
        }

        public virtual void RecordArrayElementFixup(long arrayToBeFixed, int index, long objectRequired)
        {
            throw new PlatformNotSupportedException(SR.Serialization_ObjectGraphNotSupported);
        }

        public virtual void RecordArrayElementFixup(long arrayToBeFixed, int[] indices, long objectRequired)
        {
            throw new PlatformNotSupportedException(SR.Serialization_ObjectGraphNotSupported);
        }

        public virtual void RaiseDeserializationEvent()
        {
        }

        [RequiresUnreferencedCode(ObjectManagerUnreferencedCodeMessage)]
        public void RaiseOnDeserializingEvent(object obj)
        {
            throw new PlatformNotSupportedException(SR.Serialization_CallbackDiscoveryNotSupported);
        }
    }
}
