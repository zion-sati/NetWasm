// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de resources;
// only the strings this namespace references are included.

namespace System.ComponentModel
{
    internal static class SR
    {
        internal const string CollectionConverterText =
            "(Collection)";
        internal const string ComObjectDescriptorsNotSupported =
            "COM object type descriptor functionality has been disabled in the app configuration and is not supported.";
        internal const string ConvertInvalidPrimitive =
            "{0} is not a valid value for {1}.";
        internal const string ConvertToException =
            "'{0}' is unable to convert '{1}' to '{2}'.";
        internal const string CustomTypeProviderNotImplemented =
            "Custom type providers must implement member {0}.";
        internal const string InvalidArgumentValue =
            "Value of '{0}' cannot be empty.";
        internal const string InvalidNullArgument =
            "Null is not a valid value for {0}.";
        internal const string LicMgrAlreadyLocked =
            "The CurrentContext property of the LicenseManager is already locked by another user.";
        internal const string LicMgrContextCannotBeChanged =
            "The CurrentContext property of the LicenseManager is currently locked and cannot be changed.";
        internal const string LicMgrDifferentUser =
            "The CurrentContext property of the LicenseManager can only be unlocked with the same contextUser.";
        internal const string MemberRelationshipService_RelationshipNotSupported =
            "Relationships between {0}.{1} and {2}.{3} are not supported.";
        internal const string NullableConverterBadCtorArg =
            "The specified type is not a nullable type.";
        internal const string TypeDescriptorAlreadyAssociated =
            "The primary and secondary objects are already associated with each other.";
        internal const string TypeDescriptorArgsCountMismatch =
            "The number of elements in the Type and Object arrays must match.";
        internal const string TypeDescriptorExpectedElementType =
            "Expected types in the collection to be of type {0}.";
        internal const string TypeDescriptorProviderError =
            "The type description provider {0} has returned null from {1} which is illegal.";
        internal const string TypeDescriptorSameAssociation =
            "Cannot create an association when the primary and secondary objects are the same.";
        internal const string TypeIsNotRegistered =
            "The type {0} is not registered. Call 'TypeDescriptor.RegisterType()' to register the type.";
        internal const string BinaryFormatterMessage =
            "BinaryFormatter serialization is obsolete and should not be used. See https://aka.ms/binaryformatter for more information.";
        internal const string ErrorInvalidServiceInstance =
            "The service instance must derive from or implement {0}.";
        internal const string ErrorServiceExists =
            "The service {0} already exists in the service container.";
        internal const string CultureInfoConverterDefaultCultureString =
            "(Default)";
        internal const string CultureInfoConverterInvalidCulture =
            "The {0} culture cannot be converted to a CultureInfo object on this computer.";
        internal const string MaskedTextProviderInvalidCharError =
            "The specified character value is not allowed for this property.";
        internal const string MaskedTextProviderMaskInvalidChar =
            "The specified mask contains invalid characters.";
        internal const string MaskedTextProviderPasswordAndPromptCharError =
            "The PasswordChar and PromptChar values cannot be the same.";

        // Upstream SR answers from the System.Resources.UseSystemResourceKeys switch.
        internal static bool UsingResourceKeys() =>
            AppContext.TryGetSwitch("System.Resources.UseSystemResourceKeys", out bool usingResourceKeys) && usingResourceKeys;

        // NetWasm strings for its platform boundaries.
        internal const string PlatformNotSupported_LicenseBinaryFormatter =
            "BinaryFormatter license serialization is not available on this NetWasm platform.";
        internal const string TypeDescriptorMetadataDiscoveryNotSupported =
            "Discovering members and attributes of a type is not supported on NetWasm. Register a TypeDescriptionProvider or implement ICustomTypeDescriptor to describe the type.";
        internal const string TypeDescriptorTypeActivationNotSupported =
            "Constructing a type chosen at run time is not supported on NetWasm. Supply instances through a TypeDescriptionProvider.";
        internal const string TypeDescriptorReflectedDescriptorNotSupported =
            "Creating reflected member descriptors and designers is not supported on NetWasm.";
        internal const string TypeDescriptorTypeEnumerationNotSupported =
            "Refreshing the types of a module or assembly is not supported on NetWasm.";
        internal const string LicMgrActivationNotSupported =
            "Creating a licensed component from a type chosen at run time is not supported on NetWasm.";
        internal const string LicMgrProviderDiscoveryNotSupported =
            "Discovering a type's LicenseProviderAttribute is not supported on NetWasm. Call the license provider directly.";
        internal const string LicFileProviderFileNotSupported =
            "Reading license files next to the component assembly is not supported on NetWasm. Supply the license key through the license context.";
        internal const string NullableConverterInstanceDescriptorNotSupported =
            "Converting a nullable value to an InstanceDescriptor needs constructor metadata, which is not supported on NetWasm.";
        internal const string ComponentResourceManagerReflectionNotSupported =
            "Applying resources to properties through reflection is not supported on NetWasm. Site the component in design mode to apply resources through its property descriptors.";

        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);

        internal static string Format(string format, params object?[] args) =>
            string.Format(format, args);
    }
}
