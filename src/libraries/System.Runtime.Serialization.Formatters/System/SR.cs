// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Runtime.Serialization.Formatters uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Runtime.Serialization.Formatters's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string Serialization_NoID = "Object has never been assigned an objectID.";
        internal const string Serialization_FieldReflectionNotSupported = "Reading or writing an object's fields through reflection is not supported on NetWasm. Implement ISerializable or an ISerializationSurrogate to transfer the data explicitly.";
        internal const string Serialization_TypeLoadNotSupported = "Resolving a type by name from an assembly is not supported on NetWasm.";
        internal const string BinaryFormatter_Removed = "BinaryFormatter serialization and deserialization have been removed. See https://aka.ms/binaryformatter for more information.";
        internal const string Serialization_TooManyElements = "The internal array cannot expand to greater than Int32.MaxValue elements.";
        internal const string ArgumentOutOfRange_ObjectID = "objectID cannot be less than or equal to zero.";
        internal const string Serialization_CallbackDiscoveryNotSupported = "Discovering an object's serialization callback methods is not supported on NetWasm.";
        internal const string Serialization_ObjectGraphNotSupported = "Registering objects and fixups with ObjectManager is not supported on NetWasm: completing them needs reflection over the objects' fields, constructors and callback methods.";
        internal const string Serialization_SurrogateCycle = "Adding selector will introduce a cycle.";
        internal const string Serialization_SurrogateCycleInArgument = "Selector contained a cycle.";
        internal const string Arg_HTCapacityOverflow = "Hashtable's capacity overflowed and went negative. Check load factor, capacity and the current size of the table.";
    }
}
