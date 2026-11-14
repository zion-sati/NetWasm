// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
//
// NetWasm deviations:
// - GetSerializableMembers, GetObjectData, PopulateObjectMembers and GetTypeFromAssembly throw
//   PlatformNotSupportedException: they enumerate, read or write fields through FieldInfo, or
//   resolve a type by name from an assembly. The member cache, MemberHolder and
//   SerializationFieldInfo that only they use are not ported, nor are the internal helpers that
//   only BinaryFormatter's removed implementation calls.

#pragma warning disable SYSLIB0050 // Upstream's Formatters project suppresses the legacy formatter obsoletion.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization.Formatters;

namespace System.Runtime.Serialization
{
    [Obsolete(Obsoletions.LegacyFormatterMessage, DiagnosticId = Obsoletions.LegacyFormatterDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
    public static class FormatterServices
    {
        public static MemberInfo[] GetSerializableMembers(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
        {
            return GetSerializableMembers(type, new StreamingContext(StreamingContextStates.All));
        }

        public static MemberInfo[] GetSerializableMembers(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type,
            StreamingContext context)
        {
            throw new PlatformNotSupportedException(SR.Serialization_FieldReflectionNotSupported);
        }

        public static void CheckTypeSecurity(Type t, TypeFilterLevel securityLevel)
        {
            // nop
        }

        public static object GetUninitializedObject(
            // This API doesn't call any constructors, but the type needs to be seen as constructed.
            // A type is seen as constructed if a constructor is kept.
            // This obviously won't cover a type with no constructor. Reference types with no
            // constructor are an academic problem. Valuetypes with no constructors are a problem,
            // but IL Linker currently treats them as always implicitly boxed.
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
            Type type) => RuntimeHelpers.GetUninitializedObject(type);

        public static object GetSafeUninitializedObject(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
            Type type) => RuntimeHelpers.GetUninitializedObject(type);

        public static object PopulateObjectMembers(object obj, MemberInfo[] members, object?[] data)
        {
            throw new PlatformNotSupportedException(SR.Serialization_FieldReflectionNotSupported);
        }

        public static object?[] GetObjectData(object obj, MemberInfo[] members)
        {
            throw new PlatformNotSupportedException(SR.Serialization_FieldReflectionNotSupported);
        }

        public static ISerializationSurrogate GetSurrogateForCyclicalReference(ISerializationSurrogate innerSurrogate)
        {
            ArgumentNullException.ThrowIfNull(innerSurrogate);

            return new SurrogateForCyclicalReference(innerSurrogate);
        }

        [RequiresUnreferencedCode("Types might be removed")]
        public static Type? GetTypeFromAssembly(Assembly assem, string name)
        {
            throw new PlatformNotSupportedException(SR.Serialization_TypeLoadNotSupported);
        }
    }

    internal sealed class SurrogateForCyclicalReference : ISerializationSurrogate
    {
        private readonly ISerializationSurrogate _innerSurrogate;

        internal SurrogateForCyclicalReference(ISerializationSurrogate innerSurrogate)
        {
            Debug.Assert(innerSurrogate != null);
            _innerSurrogate = innerSurrogate;
        }

        public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
        {
            _innerSurrogate.GetObjectData(obj, info, context);
        }

        public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector? selector)
        {
            return _innerSurrogate.SetObjectData(obj, info, context, selector);
        }
    }
}
