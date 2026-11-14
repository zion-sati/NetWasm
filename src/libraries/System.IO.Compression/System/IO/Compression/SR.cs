// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce resources;
// only the strings this namespace references are included.

namespace System.IO.Compression
{
    internal static class SR
    {
        internal const string CDCorrupt =
            "Central Directory corrupt.";
        internal const string CannotBeReadInCreateMode =
            "Cannot open entry for reading when archive is opened in create mode.";
        internal const string CannotBeWrittenInReadMode =
            "Cannot open entry for writing when archive is opened in read-only mode.";
        internal const string CentralDirectoryInvalid =
            "Central Directory is invalid.";
        internal const string CrcMismatch =
            "The CRC32 checksum of the extracted data does not match the expected value from the archive.";
        internal const string CreateInReadMode =
            "Cannot create entries on an archive opened in read mode.";
        internal const string CreateModeCapabilities =
            "Cannot use create mode on a non-writable stream.";
        internal const string CreateModeCreateEntryWhileOpen =
            "Entries cannot be created while previously created entries are still open.";
        internal const string CreateModeWriteOnceAndOneEntryAtATime =
            "Entries in create mode may only be written to once, and only one entry may be held open at a time.";
        internal const string DateTimeOutOfRange =
            "The DateTimeOffset specified cannot be converted into a Zip file timestamp.";
        internal const string DeleteOnlyInUpdate =
            "Delete can only be used when the archive is in Update mode.";
        internal const string DeleteOpenEntry =
            "Cannot delete an entry currently open for writing.";
        internal const string DeletedEntry =
            "Cannot modify deleted entry.";
        internal const string EOCDNotFound =
            "End of Central Directory record could not be found.";
        internal const string EmptyPassword =
            "A non-empty password is required for creating an encrypted entry.";
        internal const string EncryptionNotSpecified =
            "Encryption type is not specified.";
        internal const string EntriesInCreateMode =
            "Cannot access entries in Create mode.";
        internal const string EntryNameAndCommentEncodingNotSupported =
            "The specified encoding is not supported for entry names and comments.";
        internal const string EntryNamesTooLong =
            "Entry names cannot require more than 2^16 bits.";
        internal const string EntryTooLarge =
            "Entries larger than 4GB are not supported in Update mode.";
        internal const string EntryUncompressedSizeTooLargeForUpdateMode =
            "Entries with uncompressed data larger than 2GB are not supported in Update mode.";
        internal const string FieldTooBigCompressedSize =
            "Compressed Size cannot be held in an Int64.";
        internal const string FieldTooBigLocalHeaderOffset =
            "Local Header Offset cannot be held in an Int64.";
        internal const string FieldTooBigNumEntries =
            "Number of Entries cannot be held in an Int64.";
        internal const string FieldTooBigOffsetToCD =
            "Offset to Central Directory cannot be held in an Int64.";
        internal const string FieldTooBigOffsetToZip64EOCD =
            "Offset to Zip64 End Of Central Directory record cannot be held in an Int64.";
        internal const string FieldTooBigUncompressedSize =
            "Uncompressed Size cannot be held in an Int64.";
        internal const string FrozenAfterWrite =
            "Cannot modify entry in Create mode after entry has been opened for writing.";
        internal const string HiddenStreamName =
            "A stream from ZipArchiveEntry has been disposed.";
        internal const string InvalidAesStrength =
            "Invalid AES strength value.";
        internal const string InvalidFileAccess =
            "The specified FileAccess value is not valid.";
        internal const string InvalidOffsetToZip64EOCD =
            "Invalid offset to the Zip64 End of Central Directory record.";
        internal const string InvalidPassword =
            "The password did not match.";
        internal const string LengthAfterWrite =
            "Length properties are unavailable once an entry has been opened for writing.";
        internal const string LocalFileHeaderCorrupt =
            "A local file header is corrupt.";
        internal const string NotSupported =
            "This operation is not supported.";
        internal const string NumEntriesWrong =
            "Number of entries expected in End Of Central Directory does not correspond to number of entries in Central Directory.";
        internal const string PasswordRequired =
            "A password is required for encrypted entries.";
        internal const string PlatformNotSupported_Compression =
            "System.IO.Compression is not supported on this platform.";
        internal const string ReadModeCapabilities =
            "Cannot use read mode on a non-readable stream.";
        internal const string ReadOnlyArchive =
            "Cannot modify read-only archive.";
        internal const string ReadingNotSupported =
            "This stream from ZipArchiveEntry does not support reading.";
        internal const string SeekingNotSupported =
            "This stream from ZipArchiveEntry does not support seeking.";
        internal const string SetLengthRequiresSeekingAndWriting =
            "SetLength requires a stream that supports seeking and writing.";
        internal const string SplitSpanned =
            "Split or spanned archives are not supported.";
        internal const string TruncatedZipCryptoHeader =
            "The encryption header is truncated.";
        internal const string UnexpectedStreamLength =
            "The decompressed data length does not match the expected value from the archive.";
        internal const string UnsupportedCompression =
            "The archive entry was compressed using an unsupported compression method.";
        internal const string UnsupportedEncryptionMethod =
            "The entry's encryption method is not supported.";
        internal const string UpdateModeCapabilities =
            "Update mode requires a stream with read, write, and seek capabilities.";
        internal const string UpdateModeOneStream =
            "Entries cannot be opened multiple times in Update mode.";
        internal const string WinZipEncryptionNotSupportedOnPlatform =
            "WinZip AES encryption is not supported on this platform.";
        internal const string WritingNotSupported =
            "This stream from ZipArchiveEntry does not support writing.";
        internal const string Zip64EOCDNotWhereExpected =
            "Zip 64 End of Central Directory Record not where indicated.";

        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);
        internal const string ZLibErrorDLLLoadError = "The underlying compression routine could not be loaded correctly.";
        internal const string ZLibErrorIncorrectInitParameters = "The underlying compression routine received incorrect initialization parameters.";
        internal const string ZLibErrorNotEnoughMemory = "The underlying compression routine could not reserve sufficient memory.";
        internal const string ZLibErrorUnexpected = "The underlying compression routine returned an unexpected error code: '{0}'.";
        internal const string ZLibErrorVersionMismatch = "The version of the underlying compression routine does not match expected version.";
        internal const string CannotWriteToDeflateStream = "Writing to the compression stream is not supported.";
        internal const string GenericInvalidData = "Found invalid data while decoding.";
        internal const string InvalidBeginCall = "Only one asynchronous reader or writer is allowed time at one time.";
        internal const string NotSupported_UnreadableStream = "Stream does not support reading.";
        internal const string InvalidHuffmanData = "Failed to construct a huffman tree using the length array. The stream might be corrupted.";
        internal const string InvalidBlockLength = "Block length does not match with its complement.";
        internal const string UnknownBlockType = "Unknown block type. Stream might be corrupted.";
        internal const string UnknownState = "Decoder is in some unknown state. This might be caused by corrupted data.";
        internal const string ArgumentOutOfRange_Enum = "Enum value was out of legal range.";
        internal const string CannotReadFromDeflateStream = "Reading from the compression stream is not supported.";
        internal const string NotSupported_UnwritableStream = "Stream does not support writing.";
        internal const string TruncatedData = "Found truncated data while decoding.";
        internal const string ZLibErrorInconsistentStream = "The stream state of the underlying compression routine is inconsistent.";
        internal const string PlatformNotSupported_ZstandardCompression = "Zstandard compression is not supported on this platform.";
    }
}
