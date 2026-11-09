// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de resources;
// only the strings this namespace references are included.

namespace System.Formats.Asn1
{
    internal static class SR
    {
        internal const string ArgumentOutOfRange_NeedNonNegNum =
            "Non-negative number required.";
        internal const string Argument_DestinationTooShort =
            "Destination is too short.";
        internal const string Argument_EnumeratedValueBackingTypeNotSupported =
            "Enumerations with a backing type of '{0}' are not supported for ReadEnumeratedValue.";
        internal const string Argument_EnumeratedValueRequiresNonFlagsEnum =
            "ASN.1 Enumerated values only apply to enum types without the [Flags] attribute.";
        internal const string Argument_IntegerCannotBeEmpty =
            "An integer value cannot be empty.";
        internal const string Argument_IntegerRedundantByte =
            "The first 9 bits of the integer value all have the same value. Ensure the input is in big-endian byte order and that all redundant leading bytes have been removed.";
        internal const string Argument_InvalidOidValue =
            "The OID value is invalid.";
        internal const string Argument_NamedBitListRequiresFlagsEnum =
            "Named bit list operations require an enum with the [Flags] attribute.";
        internal const string Argument_SourceOverlapsDestination =
            "The destination buffer overlaps the source buffer.";
        internal const string Argument_Tag_NotCharacterString =
            "The specified tag has the Universal TagClass, but the TagValue does not correspond with a known character string type.";
        internal const string Argument_UniversalValueIsFixed =
            "Tags with TagClass Universal must have the appropriate TagValue value for the data type being read or written.";
        internal const string Argument_UnusedBitCountMustBeZero =
            "Unused bit count must be 0 when the bit string is empty.";
        internal const string Argument_UnusedBitCountRange =
            "Unused bit count must be between 0 and 7, inclusive.";
        internal const string Argument_UnusedBitWasSet =
            "One or more of the bits covered by the provided unusedBitCount value is set. All unused bits must be cleared.";
        internal const string Argument_WriteEncodedValue_OneValueAtATime =
            "The input to WriteEncodedValue must represent a single encoded value with no trailing data.";
        internal const string AsnWriter_EncodeUnbalancedStack =
            "Encode cannot be called while a Sequence, Set-Of, or Octet String is still open.";
        internal const string AsnWriter_ModifyingWhileEncoding =
            "The AsnWriter cannot be written to while performing the Encode callback.";
        internal const string AsnWriter_PopWrongTag =
            "Cannot pop the requested tag as it is not currently in progress.";
        internal const string ContentException_CerRequiresIndefiniteLength =
            "A constructed tag used a definite length encoding, which is invalid for CER data. The input may be encoded with BER or DER.";
        internal const string ContentException_ConstructedEncodingRequired =
            "The encoded value uses a primitive encoding, which is invalid for '{0}' values.";
        internal const string ContentException_DefaultMessage =
            "The ASN.1 value is invalid.";
        internal const string ContentException_EnumeratedValueTooBig =
            "The encoded enumerated value is larger than the value size of the '{0}' enum.";
        internal const string ContentException_InvalidTag =
            "The provided data does not represent a valid tag.";
        internal const string ContentException_InvalidUnderCerOrDer_TryBer =
            "The encoded value is not valid under the selected encoding, but it may be valid under the BER encoding.";
        internal const string ContentException_InvalidUnderCer_TryBerOrDer =
            "The encoded value is not valid under the selected encoding, but it may be valid under the BER or DER encoding.";
        internal const string ContentException_InvalidUnderDer_TryBerOrCer =
            "The encoded value is not valid under the selected encoding, but it may be valid under the BER or CER encoding.";
        internal const string ContentException_LengthExceedsPayload =
            "The encoded length exceeds the number of bytes remaining in the input buffer.";
        internal const string ContentException_LengthRuleSetConstraint =
            "The encoded length is not valid under the requested encoding rules, the value may be valid under the BER encoding.";
        internal const string ContentException_LengthTooBig =
            "The encoded length exceeds the maximum supported by this library (Int32.MaxValue).";
        internal const string ContentException_NamedBitListValueTooBig =
            "The encoded named bit list value is larger than the value size of the '{0}' enum.";
        internal const string ContentException_OidTooBig =
            "The encoded object identifier (OID) exceeds the limits supported by this library. Supported OIDs are limited to 64 arcs and each subidentifier is limited to a 128-bit value.";
        internal const string ContentException_PrimitiveEncodingRequired =
            "The encoded value uses a constructed encoding, which is invalid for '{0}' values.";
        internal const string ContentException_SetOfNotSorted =
            "The encoded set is not sorted as required by the current encoding rules. The value may be valid under the BER encoding, or you can ignore the sort validation by specifying skipSortValidation=true.";
        internal const string ContentException_TooMuchData =
            "The last expected value has been read, but the reader still has pending data. This value may be from a newer schema, or is corrupt.";
        internal const string ContentException_WrongTag =
            "The provided data is tagged with '{0}' class value '{1}', but it should have been '{2}' class value '{3}'.";
        internal const string Cryptography_Der_Invalid_Encoding =
            "ASN1 corrupted data.";

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
