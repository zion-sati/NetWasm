// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de resources;
// only the strings this namespace references are included.

namespace System.Resources
{
    internal static class SR
    {
        internal const string Arg_ResMgrNotResSet =
            "Type parameter must refer to a subclass of ResourceSet.";
        internal const string Arg_ResourceFileUnsupportedVersion =
            "The ResourceReader class does not know how to read this version of .resources files. Expected version: {0}  This file: {1}";
        internal const string Arg_ResourceNameNotExist =
            "The specified resource name \"{0}\" does not exist in the resource file.";
        internal const string ArgumentOutOfRange_StreamLength =
            "Stream length must be non-negative and less than the maximum array length {0} - origin.";
        internal const string Argument_InvalidResourceCultureName =
            "The given culture name '{0}' cannot be used to locate a resource file. Resource filenames must consist of only letters, numbers, hyphens or underscores.";
        internal const string Argument_StreamNotReadable =
            "Stream was not readable.";
        internal const string Argument_StreamNotWritable =
            "Stream was not writable.";
        internal const string BadImageFormat_InvalidType =
            "Corrupt .resources file.  The specified type doesn't exist.";
        internal const string BadImageFormat_NegativeStringLength =
            "Corrupt .resources file. String length must be non-negative.";
        internal const string BadImageFormat_ResourceDataLengthInvalid =
            "Corrupt .resources file.  The specified data length '{0}' is not a valid position in the stream.";
        internal const string BadImageFormat_ResourceNameCorrupted =
            "Corrupt .resources file. A resource name extends past the end of the stream.";
        internal const string BadImageFormat_ResourceNameCorrupted_NameIndex =
            "Corrupt .resources file. The resource name for name index {0} extends past the end of the stream.";
        internal const string BadImageFormat_ResourcesDataInvalidOffset =
            "Corrupt .resources file. Invalid offset '{0}' into data section.";
        internal const string BadImageFormat_ResourcesHeaderCorrupted =
            "Corrupt .resources file. Unable to read resources from this file because of invalid header information. Try regenerating the .resources file.";
        internal const string BadImageFormat_ResourcesIndexTooLong =
            "Corrupt .resources file. String for name index '{0}' extends past the end of the file.";
        internal const string BadImageFormat_ResourcesNameInvalidOffset =
            "Corrupt .resources file. Invalid offset '{0}' into name section.";
        internal const string BadImageFormat_ResourcesNameTooLong =
            "Corrupt .resources file. Resource name extends past the end of the file.";
        internal const string BadImageFormat_TypeMismatch =
            "Corrupt .resources file.  The specified type doesn't match the available data in the stream.";
        internal const string BinaryFormatter_SerializationDisallowed =
            "BinaryFormatter serialization and deserialization are disabled within this application. See https://aka.ms/binaryformatter for more information.";
        internal const string InvalidOperation_EnumEnded =
            "Enumeration already finished.";
        internal const string InvalidOperation_EnumNotStarted =
            "Enumeration has not started. Call MoveNext.";
        internal const string InvalidOperation_ResourceNotStream_Name =
            "Resource '{0}' was not a Stream - call GetObject instead.";
        internal const string InvalidOperation_ResourceNotString_Name =
            "Resource '{0}' was not a String - call GetObject instead.";
        internal const string InvalidOperation_ResourceNotString_Type =
            "Resource was of type '{0}' instead of String - call GetObject instead.";
        internal const string InvalidOperation_ResourceWriterSaved =
            "The resource writer has already been closed and cannot be edited.";
        internal const string NotSupported_BinarySerializedResources =
            "This platform does not support binary serialized resources.";
        internal const string NotSupported_ResourceObjectSerialization =
            "Cannot read resources that depend on serialization.";
        internal const string NotSupported_UnseekableStream =
            "Stream does not support seeking.";
        internal const string NotSupported_WrongResourceReader_Type =
            "This .resources file should not be read with this reader. The resource reader type is \"{0}\".";
        internal const string ObjectDisposed_ResourceSet =
            "Cannot access a closed resource set.";
        internal const string ResourceManager_ReflectionNotAllowed =
            "Use of ResourceManager for custom types is disabled. Set the MSBuild Property CustomResourceTypesSupport to true in order to enable it.";
        internal const string ResourceReaderIsClosed =
            "ResourceReader is closed.";
        internal const string Resources_StreamNotValid =
            "Stream is not a valid resource file.";
        internal const string Arg_InvalidNeutralResourcesLanguage_FallbackLoc =
            "The NeutralResourcesLanguageAttribute specifies an invalid or unrecognized ultimate resource fallback location: \"{0}\".";

        // NetWasm strings for its resource boundaries.
        internal const string PlatformNotSupported_ResourceManagerAssembly =
            "NetWasm cannot load resources from an assembly's manifest or satellite assemblies; supply resource sets by overriding InternalGetResourceSet.";
        internal const string PlatformNotSupported_ResourceManagerFiles =
            "NetWasm does not create file-based resource managers; supply resource sets by overriding InternalGetResourceSet.";
        internal const string PlatformNotSupported_ResourceFiles =
            "NetWasm reads and writes .resources data through a caller-supplied stream, not a file name.";
        internal const string PlatformNotSupported_ResourceDeserialization =
            "NetWasm cannot deserialize custom resource types: BinaryFormatter is not available on this platform.";

        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);
    }
}
