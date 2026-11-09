// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de resources;
// only the strings this namespace references are included.

namespace System.Security.Cryptography
{
    internal static class SR
    {
        internal const string Cryptography_IncorrectTagLength =
            "The size of the specified tag does not match the expected size of {0}.";
        internal const string Cryptography_InvalidNonceLength =
            "The specified nonce is not a valid size for this algorithm.";
        internal const string Cryptography_InvalidTagLength =
            "The specified tag is not a valid size for this algorithm.";
        internal const string Cryptography_KeyWrap_Ciphertext_InvalidLength =
            "The ciphertext is not a valid length for the Key Wrap algorithm.";
        internal const string Cryptography_KeyWrap_Plaintext_InvalidLength =
            "The plaintext is not a valid length for the Key Wrap algorithm.";
        internal const string Cryptography_PlaintextCiphertextLengthMismatch =
            "Plaintext and ciphertext must have the same length.";
        internal const string Argument_PemEncoding_EncodedSizeTooLarge =
            "The encoded PEM size is too large to represent as a signed 32-bit integer.";
        internal const string Argument_PemEncoding_InvalidLabel =
            "The specified label is not valid.";
        internal const string Argument_PemEncoding_NoPemFound =
            "No PEM encoded data found.";
        internal const string Argument_PemImport_AmbiguousPem =
            "The input contains multiple keys, but only one key can be imported.";
        internal const string Argument_PemImport_EncryptedPem =
            "An encrypted key was found, but no password was provided. Use ImportFromEncryptedPem to import this key.";
        internal const string Argument_PemImport_NoPemFound =
            "No supported key formats were found. Check that the input represents the contents of a PEM-encoded key file, not the path to such a file.";
        internal const string Cryptography_AlgKdfRequiresChars =
            "The KDF for algorithm '{0}' requires a char-based password input.";
        internal const string Cryptography_CSP_NoPrivateKey =
            "Object contains only the public half of a key pair. A private key must also be provided.";
        internal const string Cryptography_Der_Invalid_Encoding =
            "ASN1 corrupted data.";
        internal const string Cryptography_ECC_NamedCurvesOnly =
            "Only named curves are supported on this platform.";
        internal const string Cryptography_ECXmlSerializationFormatRequired =
            "XML serialization of an elliptic curve key requires using an overload which specifies the XML format to be used.";
        internal const string Cryptography_FormatterMissingAlgorithm =
            "No hash algorithm has been associated with this formatter object, assign one via the SetHashAlgorithm method.";
        internal const string Cryptography_FormatterMissingKey =
            "No asymmetric key object has been associated with this formatter object, assign one via the SetKey method.";
        internal const string Cryptography_FromXmlParseError =
            "The provided XML could not be read.";
        internal const string Cryptography_InvalidFromXmlString =
            "Input string does not contain a valid encoding of the '{0}' '{1}' parameter.";
        internal const string Cryptography_InvalidOperation =
            "This operation is not supported for this class.";
        internal const string Cryptography_InvalidRsaParameters =
            "The specified RSA parameters are not valid. Exponent and Modulus are required. If D is present, it must have the same length as Modulus. If D is present, P, Q, DP, DQ, and InverseQ are required and must have half the length of Modulus, rounded up, otherwise they must be omitted.";
        internal const string Cryptography_NotValidPrivateKey =
            "Key is not a valid private key.";
        internal const string Cryptography_NotValidPublicOrPrivateKey =
            "Key is not a valid public or private key.";
        internal const string Cryptography_Pkcs8_EncryptedReadFailed =
            "The EncryptedPrivateKeyInfo structure was decoded but was not successfully interpreted, the password may be incorrect.";
        internal const string Cryptography_RSAPrivateKey_VersionTooNew =
            "The provided RSAPrivateKey value has version '{0}', but version '{1}' is the maximum supported.";
        internal const string Cryptography_UnknownAlgorithmIdentifier =
            "The algorithm identified by '{0}' is unknown, not valid for the requested usage, or was not handled.";
        internal const string Cryptography_UnknownSignatureFormat =
            "The signature format '{0}' is unknown.";
        internal const string NotSupported_Method =
            "Method not supported.";
        internal const string Cryptography_ConcurrentUseNotSupported =
            "Concurrent operations from multiple threads on this type are not supported.";
        internal const string ArgumentOutOfRange_Generic_MustBeNonNegativeNonZero =
            "{0} ('{1}') must be a non-negative and non-zero value.";
        internal const string Cryptography_MissingIV =
            "The cipher mode specified requires that an initialization vector (IV) be used.";
        internal const string Arg_EmptyOrNullArray =
            "Array may not be empty or null.";
        internal const string Arg_EmptySpan =
            "Span may not be empty.";
        internal const string Arg_RankMultiDimNotSupported =
            "Only single dimensional arrays are supported for the requested action.";
        internal const string ArgumentOutOfRange_IndexMustBeLess =
            "Index was out of range. Must be non-negative and less than the size of the collection.";
        internal const string ArgumentOutOfRange_NeedNonNegNum =
            "Non-negative number required.";
        internal const string Argument_BitsMustBeWholeBytes =
            "The value specified in bits must be a whole number of bytes.";
        internal const string Argument_DestinationImprecise =
            "Destination must be exactly {0} bytes.";
        internal const string Cryptography_AlgorithmNotSupported =
            "Algorithm '{0}' is not supported on this platform.";
        internal const string Cryptography_CipherModeFeedbackNotSupported =
            "The specified feedback size '{0}' for CipherMode '{1}' is not supported.";
        internal const string Cryptography_CipherModeNotSupported =
            "The specified CipherMode '{0}' is not supported.";
        internal const string Cryptography_CurveNotSupported =
            "The specified curve '{0}' or its parameters are not valid for this platform.";
        internal const string Cryptography_EncryptedIncorrectLength =
            "{0} unexpectedly produced a ciphertext with the incorrect length.";
        internal const string Cryptography_InvalidBlockSize =
            "Specified block size is not valid for this algorithm.";
        internal const string Cryptography_InvalidCipherMode =
            "Specified cipher mode is not valid for this algorithm.";
        internal const string Cryptography_InvalidCurveKeyParameters =
            "The specified key parameters are not valid. Q.X and Q.Y, or D, must be specified. Q.X, Q.Y must be the same length. If D is specified it must be the same length as Q.X and Q.Y if also specified for named curves or the same length as Order for explicit curves.";
        internal const string Cryptography_InvalidCurveOid =
            "The specified Oid ({0}) is not valid. The Oid.FriendlyName or Oid.Value property must be set.";
        internal const string Cryptography_InvalidECCharacteristic2Curve =
            "The specified Characteristic2 curve parameters are not valid. Polynomial, A, B, G.X, G.Y, and Order are required. A, B, G.X, G.Y must be the same length, and the same length as Q.X, Q.Y and D if those are specified. Cofactor is required. Seed and Hash are optional. Other parameters are not allowed.";
        internal const string Cryptography_InvalidECNamedCurve =
            "The specified named curve parameters are not valid. Only the Oid parameter must be set.";
        internal const string Cryptography_InvalidECPrimeCurve =
            "The specified prime curve parameters are not valid. Prime, A, B, G.X, G.Y and Order are required and must be the same length, and the same length as Q.X, Q.Y and D if those are specified. Cofactor is required. Seed and Hash are optional. Other parameters are not allowed.";
        internal const string Cryptography_InvalidFeedbackSize =
            "Specified feedback size is not valid for this algorithm.";
        internal const string Cryptography_InvalidIVSize =
            "Specified initialization vector (IV) does not match the block size for this algorithm.";
        internal const string Cryptography_InvalidKeySize =
            "Specified key is not a valid size for this algorithm.";
        internal const string Cryptography_InvalidKey_SemiWeak =
            "Specified key is a known semi-weak key for '{0}' and cannot be used.";
        internal const string Cryptography_InvalidKey_Weak =
            "Specified key is a known weak key for '{0}' and cannot be used.";
        internal const string Cryptography_InvalidPadding =
            "Padding is invalid and cannot be removed.";
        internal const string Cryptography_InvalidPaddingMode =
            "Specified padding mode is not valid for this algorithm.";
        internal const string Cryptography_KeyWrap_DecryptFailed =
            "The ciphertext did not successfully decrypt with the instance key.";
        internal const string Cryptography_KeyWrap_InvalidLength =
            "The ciphertext is not a valid length for the Key Wrap with Padding Unwrap algorithm.";
        internal const string Cryptography_MatchBlockSize =
            "The specified plaintext size is not valid for the padding and block size.";
        internal const string Cryptography_MatchFeedbackSize =
            "The specified plaintext size is not valid for the padding and feedback size.";
        internal const string Cryptography_MustTransformWholeBlock =
            "TransformBlock may only process bytes in block sized increments.";
        internal const string Cryptography_Oid_InvalidName =
            "No OID value matches this name.";
        internal const string Cryptography_Oid_InvalidValue =
            "The OID value is invalid.";
        internal const string Cryptography_Oid_SetOnceFriendlyName =
            "The OID friendly name cannot be changed from its current value.";
        internal const string Cryptography_Oid_SetOnceValue =
            "The OID value cannot be changed from its current value.";
        internal const string Cryptography_OverlappingBuffers =
            "One or more provided buffers overlap.";
        internal const string Cryptography_PartialBlock =
            "The input data is not a complete block.";
        internal const string Cryptography_PlaintextTooLarge =
            "The specified plaintext size is too large.";
        internal const string Cryptography_RC2_EKS40 =
            "EffectiveKeySize value must be at least 40 bits.";
        internal const string Cryptography_RC2_EKSKS =
            "KeySize value must be at least as large as the EffectiveKeySize value.";
        internal const string Cryptography_RC2_EKSKS2 =
            "EffectiveKeySize must be the same as KeySize in this implementation.";
        internal const string Cryptography_Rijndael_BlockSize =
            "BlockSize must be 128 in this implementation.";
        internal const string Cryptography_UnknownPaddingMode =
            "Unknown padding mode used.";
        internal const string InvalidOperation_UnsupportedBlockSize =
            "The algorithm's block size is not supported.";
        internal const string NotSupported_SubclassOverride =
            "Method not supported. Derived class must override.";
        internal const string Arg_CryptographyException =
            "Error occurred during a cryptographic operation.";
        internal const string Arg_EnumIllegalVal =
            "Illegal enum value: {0}.";
        internal const string Argument_DestinationTooShort =
            "Destination is too short.";
        internal const string Argument_HashImprecise =
            "Hash must be exactly {0} bytes.";
        internal const string Argument_InvalidOffLen =
            "Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.";
        internal const string Argument_InvalidValue =
            "Value was invalid.";
        internal const string Argument_StreamNotReadable =
            "Stream was not readable.";
        internal const string Argument_StreamNotWritable =
            "Stream was not writable.";
        internal const string CryptoConfigNotSupported =
            "Accessing a hash algorithm by manipulating the HashName property is not supported on this platform. Instead, you must instantiate one of the supplied subtypes (such as HMACSHA1.)";
        internal const string Cryptography_CAPI_Required =
            "'{0}' requires Windows Cryptographic API (CAPI), which is not available on this platform.";
        internal const string Cryptography_Config_EncodedOIDError =
            "Encoded OID length is too large (greater than 0x7f bytes).";
        internal const string Cryptography_CryptoStream_FlushFinalBlockTwice =
            "FlushFinalBlock() method was called twice on a CryptoStream. It can only be called once.";
        internal const string Cryptography_DefaultAlgorithm_NotSupported =
            "This platform does not allow the automatic selection of an algorithm.";
        internal const string Cryptography_ExceedKdfExtractLimit =
            "The total number of bytes extracted cannot exceed UInt32.MaxValue * hash length.";
        internal const string Cryptography_HashAlgorithmNameNullOrEmpty =
            "The hash algorithm name cannot be null or empty.";
        internal const string Cryptography_HashNotYetFinalized =
            "Hash must be finalized before the hash value is retrieved.";
        internal const string Cryptography_InvalidHashAlgorithmOid =
            "The specified OID ({0}) does not represent a known hash algorithm.";
        internal const string Cryptography_InvalidOID =
            "Object identifier (OID) is unknown.";
        internal const string Cryptography_PasswordDerivedBytes_TooManyBytes =
            "Requested number of bytes exceeds the maximum.";
        internal const string Cryptography_PasswordDerivedBytes_ValuesFixed =
            "Value of '{0}' cannot be changed after the bytes have been retrieved.";
        internal const string Cryptography_UnknownHashAlgorithm =
            "'{0}' is not a known hash algorithm.";
        internal const string HashNameMultipleSetNotSupported =
            "Setting the hashname after it's already been set is not supported on this platform.";
        internal const string InvalidOperation_IncorrectImplementation =
            "The algorithm's implementation is incorrect.";
        internal const string NotSupported_UnreadableStream =
            "Stream does not support reading.";
        internal const string NotSupported_UnseekableStream =
            "Stream does not support seeking.";
        internal const string NotSupported_UnwritableStream =
            "Stream does not support writing.";
        internal const string SystemSecurityCryptography_PlatformNotSupported =
            "System.Security.Cryptography is not supported on this platform.";

        internal const string Cryptography_XmlKeyParsingNotSupported =
            "Reading a key from XML is not supported on NetWasm. Import the key with ImportParameters, or from DER or PEM.";

        internal const string Arg_EmptyOrNullString =
            "String cannot be empty or null.";
        internal const string Arg_InvalidType =
            "Invalid type.";
        internal const string Argument_InvalidFlag =
            "Value of flags is invalid.";
        internal const string Argument_InvalidOidValue =
            "The OID value was invalid.";
        internal const string Argument_Thumbprint_Invalid =
            "The supplied thumbprint is not a valid hex-encoded digest.";
        internal const string Cryptography_CertReq_AlgorithmMustMatch =
            "The issuer certificate public key algorithm ({0}) does not match the value for this certificate request ({1}), use the X509SignatureGenerator overload.";
        internal const string Cryptography_CertReq_BasicConstraintsRequired =
            "The issuer certificate does not have a Basic Constraints extension.";
        internal const string Cryptography_CertReq_DatesReversed =
            "The provided notBefore value is later than the notAfter value.";
        internal const string Cryptography_CertReq_DuplicateExtension =
            "An X509Extension with OID '{0}' has already been specified.";
        internal const string Cryptography_CertReq_ExtensionRequestInOtherAttributes =
            "The OtherRequestAttributes collection contains a PKCS#9 Extension Request Attribute. This attribute cannot be manually specified, it is generated via the CertificateExtensions property.";
        internal const string Cryptography_CertReq_IssuerBasicConstraintsInvalid =
            "The issuer certificate does not have an appropriate value for the Basic Constraints extension.";
        internal const string Cryptography_CertReq_IssuerKeyUsageInvalid =
            "The issuer certificate's Key Usage extension is present but does not contain the KeyCertSign flag.";
        internal const string Cryptography_CertReq_IssuerRequiresPrivateKey =
            "The provided issuer certificate does not have an associated private key.";
        internal const string Cryptography_CertReq_MissingOidInCollection =
            "The collection in the '{0}' property contains a value that has a null Oid value.";
        internal const string Cryptography_CertReq_NoHashAlgorithmProvided =
            "The intended signature algorithm requires a HashAlgorithmName, but one was not provided when building the CertificateRequest object.";
        internal const string Cryptography_CertReq_NoKeyProvided =
            "This method cannot be used since no signing key was provided via a constructor, use an overload accepting an X509SignatureGenerator instead.";
        internal const string Cryptography_CertReq_NotAfterNotNested =
            "The requested notAfter value ({0}) is later than issuerCertificate.NotAfter ({1}).";
        internal const string Cryptography_CertReq_NotBeforeNotNested =
            "The requested notBefore value ({0}) is earlier than issuerCertificate.NotBefore ({1}).";
        internal const string Cryptography_CertReq_NullValueInCollection =
            "The collection in the '{0}' property contains a null value.";
        internal const string Cryptography_CertReq_RSAPaddingRequired =
            "The issuer certificate uses an RSA key but no RSASignaturePadding was provided to a constructor. If one cannot be provided, use the X509SignatureGenerator overload.";
        internal const string Cryptography_Cert_AlreadyHasPrivateKey =
            "The certificate already has an associated private key.";
        internal const string Cryptography_CustomTrustCertsInSystemMode =
            "Custom trust certificates were provided while in System trust mode.";
        internal const string Cryptography_InvalidContextHandle =
            "The chain context handle is invalid.";
        internal const string Cryptography_InvalidHandle =
            "{0} is an invalid handle.";
        internal const string Cryptography_InvalidTrustCertificate =
            "A null or disposed certificate was present in CustomTrustStore.";
        internal const string Cryptography_Invalid_IA5String =
            "The string contains a character not in the 7 bit ASCII character set.";
        internal const string Cryptography_PrivateKey_DoesNotMatch =
            "The provided key does not match the public key for this certificate.";
        internal const string Cryptography_PrivateKey_WrongAlgorithm =
            "The provided key does not match the public key algorithm for this certificate.";
        internal const string Cryptography_UnknownKeyAlgorithm =
            "'{0}' is not a known key algorithm.";
        internal const string Cryptography_X500_MultiValued =
            "Single-element methods are not valid on an X.500 Relative Distinguished Name that has multiple elements.";
        internal const string Cryptography_X509_ChainBuildingFailed =
            "An unknown chain building error occurred.";
        internal const string Cryptography_X509_ExportFailed =
            "The certificate export operation failed.";
        internal const string Cryptography_X509_ExtensionMismatch =
            "The parameter should be an X509Extension.";
        internal const string Cryptography_X509_InvalidContentType =
            "Invalid content type.";
        internal const string Cryptography_X509_InvalidFindType =
            "Invalid find type.";
        internal const string Cryptography_X509_InvalidFindValue =
            "Invalid find value.";
        internal const string Cryptography_X509_InvalidFlagCombination =
            "The flags '{0}' may not be specified together.";
        internal const string Cryptography_X509_StoreNotOpen =
            "The X509 certificate store has not been opened.";
        internal const string InvalidOperation_EnumNotStarted =
            "Enumeration has not started. Call MoveNext.";
        internal const string NotSupported_ImmutableX509Certificate =
            "X509Certificate is immutable on this platform. Use the equivalent constructor instead.";
        internal const string NotSupported_KeyAlgorithm =
            "The certificate key algorithm is not supported.";
        internal const string NotSupported_LegacyBasicConstraints =
            "The X509 Basic Constraints extension with OID 2.5.29.10 is not supported.";
        internal const string SystemSecurityCryptographyX509Certificates_PlatformNotSupported =
            "System.Security.Cryptography.X509Certificates is not supported on this platform.";

        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);
    }
}
