// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de resources;
// only the strings this namespace references are included.

namespace System.Runtime.Serialization
{
    internal static class SR
    {
        internal const string ArgumentOutOfRange_ObjectID =
            "objectID cannot be less than or equal to zero.";
        internal const string BinaryFormatter_Removed =
            "BinaryFormatter serialization and deserialization have been removed. See https://aka.ms/binaryformatter for more information.";
        internal const string OrderCannotBeNegative =
            "Property 'Order' in DataMemberAttribute attribute cannot be a negative number.";
        internal const string Serialization_NoID =
            "Object has never been assigned an objectID.";
        internal const string Serialization_SurrogateCycle =
            "Adding selector will introduce a cycle.";
        internal const string Serialization_SurrogateCycleInArgument =
            "Selector contained a cycle.";
        internal const string Serialization_TooManyElements =
            "The internal array cannot expand to greater than Int32.MaxValue elements.";
        internal const string Serialization_CallbackDiscoveryNotSupported =
            "Discovering an object's serialization callback methods is not supported on NetWasm.";
        internal const string Serialization_FieldReflectionNotSupported =
            "Reading or writing an object's fields through reflection is not supported on NetWasm. Implement ISerializable or an ISerializationSurrogate to transfer the data explicitly.";
        internal const string Serialization_ObjectGraphNotSupported =
            "Registering objects and fixups with ObjectManager is not supported on NetWasm: completing them needs reflection over the objects' fields, constructors and callback methods.";
        internal const string Serialization_TypeLoadNotSupported =
            "Resolving a type by name from an assembly is not supported on NetWasm.";
    }
}
