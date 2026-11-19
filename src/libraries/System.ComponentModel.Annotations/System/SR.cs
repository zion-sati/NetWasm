// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.ComponentModel.Annotations uses, from its Strings.resx at dotnet/runtime
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce, and NetWasm's own.

namespace System
{
    internal static partial class SR
    {
        internal const string AllowedValuesAttribute_Invalid = "The {0} field does not equal any of the values specified in AllowedValuesAttribute.";
        internal const string AssociatedMetadataTypeTypeDescriptor_MetadataTypeContainsUnknownProperties = "The associated metadata type for type '{0}' contains the following unknown properties or fields: {1}. Please make sure that the names of these members match the names of the properties on the main type.";
        internal const string AttributeStore_Unknown_Property = "The type '{0}' does not contain a public property named '{1}'.";
        internal const string Base64StringAttribute_Invalid = "The {0} field is not a valid Base64 encoding.";
        internal const string Common_PropertyNotFound = "The property {0}.{1} could not be found.";
        internal const string CompareAttribute_MustMatch = "'{0}' and '{1}' do not match.";
        internal const string CompareAttribute_UnknownProperty = "Could not find a property named {0}.";
        internal const string CreditCardAttribute_Invalid = "The {0} field is not a valid credit card number.";
        internal const string CustomValidationAttribute_Method_Must_Return_ValidationResult = "The CustomValidationAttribute method '{0}' in type '{1}' must return System.ComponentModel.DataAnnotations.ValidationResult.  Use System.ComponentModel.DataAnnotations.ValidationResult.Success to represent success.";
        internal const string CustomValidationAttribute_Method_Not_Found = "The CustomValidationAttribute method '{0}' does not exist in type '{1}' or is not public and static.";
        internal const string CustomValidationAttribute_Method_Required = "The CustomValidationAttribute.Method was not specified.";
        internal const string CustomValidationAttribute_Method_Signature = "The CustomValidationAttribute method '{0}' in type '{1}' must match the expected signature: public static ValidationResult {0}(object value, ValidationContext context).  The value can be strongly typed.  The ValidationContext parameter is optional.";
        internal const string CustomValidationAttribute_Type_Conversion_Failed = "Could not convert the value of type '{0}' to '{1}' as expected by method {2}.{3}.";
        internal const string CustomValidationAttribute_Type_Must_Be_Public = "The custom validation type '{0}' must be public.";
        internal const string CustomValidationAttribute_ValidationError = "{0} is not valid.";
        internal const string CustomValidationAttribute_ValidatorType_Required = "The CustomValidationAttribute.ValidatorType was not specified.";
        internal const string DataTypeAttribute_EmptyDataTypeString = "The custom DataType string cannot be null or empty.";
        internal const string DeniedValuesAttribute_Invalid = "The {0} field equals one of the values specified in DeniedValuesAttribute.";
        internal const string DisplayAttribute_PropertyNotSet = "The {0} property has not been set.  Use the {1} method to get the value.";
        internal const string EmailAddressAttribute_Invalid = "The {0} field is not a valid e-mail address.";
        internal const string EnumDataTypeAttribute_TypeCannotBeNull = "The type provided for EnumDataTypeAttribute cannot be null.";
        internal const string EnumDataTypeAttribute_TypeNeedsToBeAnEnum = "The type '{0}' needs to represent an enumeration type.";
        internal const string FileExtensionsAttribute_Invalid = "The {0} field only accepts files with the following extensions: {1}";
        internal const string LocalizableString_LocalizationFailed = "Cannot retrieve property '{0}' because localization failed.  Type '{1}' is not public or does not contain a public static string property with the name '{2}'.";
        internal const string MaxLengthAttribute_InvalidMaxLength = "MaxLengthAttribute must have a Length value that is greater than zero. Use MaxLength() without parameters to indicate that the string or array can have the maximum allowable length.";
        internal const string MaxLengthAttribute_ValidationError = "The field {0} must be a string or array type with a maximum length of '{1}'.";
        internal const string MetadataTypeAttribute_TypeCannotBeNull = "MetadataClassType cannot be null.";
        internal const string MinLengthAttribute_InvalidMinLength = "MinLengthAttribute must have a Length value that is zero or greater.";
        internal const string MinLengthAttribute_ValidationError = "The field {0} must be a string or array type with a minimum length of '{1}'.";
        internal const string LengthAttribute_InvalidMinLength = "LengthAttribute must have a MinimumLength value that is zero or greater.";
        internal const string LengthAttribute_InvalidMaxLength = "LengthAttribute must have a MaximumLength value that is greater than or equal to MinimumLength.";
        internal const string LengthAttribute_ValidationError = "The field {0} must be a string or collection type with a minimum length of '{1}' and maximum length of '{2}'.";
        internal const string LengthAttribute_InvalidValueType = "The field of type {0} must be a string, array or ICollection type.";
        internal const string PhoneAttribute_Invalid = "The {0} field is not a valid phone number.";
        internal const string RangeAttribute_ArbitraryTypeNotIComparable = "The type {0} must implement {1}.";
        internal const string RangeAttribute_MinGreaterThanMax = "The maximum value '{0}' must be greater than or equal to the minimum value '{1}'.";
        internal const string RangeAttribute_CannotUseExclusiveBoundsWhenTheyAreEqual = "Cannot use exclusive bounds when the maximum value is equal to the minimum value.";
        internal const string RangeAttribute_Must_Set_Min_And_Max = "The minimum and maximum values must be set.";
        internal const string RangeAttribute_Must_Set_Operand_Type = "The OperandType must be set when strings are used for minimum and maximum values.";
        internal const string RangeAttribute_ValidationError = "The field {0} must be between {1} and {2}.";
        internal const string RangeAttribute_ValidationError_MinExclusive = "The field {0} must be between {1} exclusive and {2}.";
        internal const string RangeAttribute_ValidationError_MaxExclusive = "The field {0} must be between {1} and {2} exclusive.";
        internal const string RangeAttribute_ValidationError_MinExclusive_MaxExclusive = "The field {0} must be between {1} exclusive and {2} exclusive.";
        internal const string RegexAttribute_ValidationError = "The field {0} must match the regular expression '{1}'.";
        internal const string RegularExpressionAttribute_Empty_Pattern = "The pattern must be set to a valid regular expression.";
        internal const string RequiredAttribute_ValidationError = "The {0} field is required.";
        internal const string StringLengthAttribute_InvalidMaxLength = "The maximum length must be a nonnegative integer.";
        internal const string StringLengthAttribute_ValidationError = "The field {0} must be a string with a maximum length of {1}.";
        internal const string StringLengthAttribute_ValidationErrorIncludingMinimum = "The field {0} must be a string with a minimum length of {2} and a maximum length of {1}.";
        internal const string UIHintImplementation_ControlParameterKeyIsNotAString = "The key parameter at position {0} with value '{1}' is not a string. Every key control parameter must be a string.";
        internal const string UIHintImplementation_ControlParameterKeyIsNull = "The key parameter at position {0} is null. Every key control parameter must be a string.";
        internal const string UIHintImplementation_ControlParameterKeyOccursMoreThanOnce = "The key parameter at position {0} with value '{1}' occurs more than once.";
        internal const string UIHintImplementation_NeedEvenNumberOfControlParameters = "The number of control parameters must be even.";
        internal const string UrlAttribute_Invalid = "The {0} field is not a valid fully-qualified http, https, or ftp URL.";
        internal const string ValidationAttribute_Cannot_Set_ErrorMessage_And_Resource = "Either ErrorMessageString or ErrorMessageResourceName must be set, but not both.";
        internal const string ValidationAttribute_IsValid_NotImplemented = "IsValid(object value) has not been implemented by this class.  The preferred entry point is GetValidationResult() and classes should override IsValid(object value, ValidationContext context).";
        internal const string ValidationAttribute_NeedBothResourceTypeAndResourceName = "Both ErrorMessageResourceType and ErrorMessageResourceName need to be set on this attribute.";
        internal const string ValidationAttribute_ResourcePropertyNotStringType = "The property '{0}' on resource type '{1}' is not a string type.";
        internal const string ValidationAttribute_ResourceTypeDoesNotHaveProperty = "The resource type '{0}' does not have an accessible static property named '{1}'.";
        internal const string ValidationAttribute_ValidationError = "The field {0} is invalid.";
        internal const string Validator_InstanceMustMatchValidationContextInstance = "The instance provided must match the ObjectInstance on the ValidationContext supplied.";
        internal const string Validator_Property_Value_Wrong_Type = "The value for property '{0}' must be of type '{1}'.";

        internal const string ValidatorReflectionNotSupported = "Validating an object or a property finds its validation attributes by reflection, which NetWasm does not support. Validate each value with Validator.TryValidateValue and the attributes it needs, or generate options validation with Microsoft.Extensions.Options' [OptionsValidator].";

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
    }
}
