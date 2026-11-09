// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de resources;
// only the strings this namespace references are included.

namespace System.IO
{
    internal static class SR
    {
        internal const string ArgumentOutOfRange_StreamPosition = "Stream position must be non-negative and less than or equal to {0}.";
        internal const string NotSupported_UnwritableStream = "Stream does not support writing.";
        internal const string IO_DriveNotFound =
            "Could not find the drive. The drive might not be ready or might not be mapped.";
        internal const string IO_NotSupported_UnreadableStream =
            "Stream does not support reading.";
        internal const string IO_NotSupported_UnseekableStream =
            "Stream does not support seeking.";
        internal const string IO_NotSupported_UnwritableStream =
            "Stream does not support writing.";
        internal const string UnauthorizedAccess_MemStreamBuffer =
            "MemoryStream's internal buffer cannot be accessed.";
        internal const string Arg_BadDecimal =
            "Read an invalid decimal value from the buffer.";
        internal const string Arg_BufferTooSmall =
            "Not enough space available in the buffer.";
        internal const string ArgumentOutOfRange_Enum =
            "Enum value was out of legal range.";
        internal const string ArgumentOutOfRange_LengthGreaterThanCapacity =
            "The length cannot be greater than the capacity.";
        internal const string ArgumentOutOfRange_PositionLessThanCapacityRequired =
            "The position may not be greater or equal to the capacity of the accessor.";
        internal const string ArgumentOutOfRange_UnmanagedMemStreamLength =
            "UnmanagedMemoryStream length must be non-negative and less than 2^63 - 1 - baseAddress.";
        internal const string ArgumentOutOfRange_UnmanagedMemStreamWrapAround =
            "The UnmanagedMemoryStream capacity would wrap around the high end of the address space.";
        internal const string Argument_InvalidOffLen =
            "Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.";
        internal const string Argument_InvalidSafeBufferOffLen =
            "Offset and length were greater than the size of the SafeBuffer.";
        internal const string Argument_InvalidSeekOrigin =
            "Invalid seek origin.";
        internal const string Argument_NotEnoughBytesToRead =
            "There are not enough bytes remaining in the accessor to read at this position.";
        internal const string Argument_NotEnoughBytesToWrite =
            "There are not enough bytes remaining in the accessor to write at this position.";
        internal const string Argument_OffsetAndCapacityOutOfBounds =
            "Offset and capacity were greater than the size of the view.";
        internal const string Argument_UnmanagedMemAccessorWrapAround =
            "The UnmanagedMemoryAccessor capacity and offset would wrap around the high end of the address space.";
        internal const string IO_FixedCapacity =
            "Unable to expand length of this stream beyond its capacity.";
        internal const string IO_SeekBeforeBegin =
            "An attempt was made to move the position before the beginning of the stream.";
        internal const string IO_StreamTooLong =
            "Stream was too long.";
        internal const string IndexOutOfRange_UMSPosition =
            "Unmanaged memory stream position was beyond the capacity of the stream.";
        internal const string InvalidOperation_CalledTwice =
            "The method cannot be called twice on the same instance.";
        internal const string NotSupported_Reading =
            "Accessor does not support reading.";
        internal const string NotSupported_UmsSafeBuffer =
            "This operation is not supported for an UnmanagedMemoryStream created from a SafeBuffer.";
        internal const string NotSupported_Writing =
            "Accessor does not support writing.";
        internal const string ObjectDisposed_ViewAccessorClosed =
            "Cannot access a closed accessor.";

        // NetWasm strings for its file-system enumeration boundaries.
        internal const string PlatformNotSupported_DirectoryEnumeration =
            "Directory enumeration through FileSystemEnumerator is not available on this NetWasm platform.";
        internal const string PlatformNotSupported_FileSystemEntryMetadata =
            "FileSystemEntry metadata is not available on this NetWasm platform because no enumeration populates it.";

        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);
        internal const string Arg_FileIsDirectory_Name = "The target file '{0}' is a directory, not a file.";
        internal const string Arg_InvalidFileAttrs = "Invalid File or Directory attributes value.";
        internal const string Arg_InvalidHandle = "Invalid handle.";
        internal const string Arg_InvalidUnixFileMode = "Invalid UnixFileMode value.";
        internal const string Arg_Path2IsRooted = "Second path fragment must not be a drive or UNC name.";
        internal const string Argument_DirectorySeparatorInvalid = "The value may not contain directory separator characters.";
        internal const string Argument_InvalidAppendMode = "Append access can be requested only in write-only mode.";
        internal const string Argument_InvalidFileModeAndAccessCombo = "Combining FileMode: {0} with FileAccess: {1} is invalid.";
        internal const string Argument_InvalidPreallocateAccess = "Preallocation size can be requested only in write mode.";
        internal const string Argument_InvalidPreallocateMode = "Preallocation size can be requested only for new files.";
        internal const string Argument_InvalidSubPath = "The directory specified, '{0}', is not a subdirectory of '{1}'.";
        internal const string Argument_InvalidUnixCreateMode = "UnixCreateMode can be requested only for modes that can create new files.";
        internal const string Argument_NullCharInPath = "Null character in path.";
        internal const string Argument_PathEmpty = "Path cannot be the empty string or all whitespace.";
        internal const string Argument_StreamNotReadable = "Stream was not readable.";
        internal const string Argument_StreamNotWritable = "Stream was not writable.";
        internal const string IO_AlreadyExists_Name = "Cannot create '{0}' because a file or directory with the same name already exists.";
        internal const string IO_CannotReplaceSameFile = "The source '{0}' and destination '{1}' are the same file.";
        internal const string IO_FileNotFound_FileName = "Could not find file '{0}'.";
        internal const string IO_FileTooLong2GB = "The file is too long. This operation is currently limited to supporting files less than 2 gigabytes in size.";
        internal const string IO_NotAFile = "The specified path '{0}' is not a file.";
        internal const string IO_PathNotFound_Path = "Could not find a part of the path '{0}'.";
        internal const string IO_SeekAppendOverwrite = "Unable seek backward to overwrite data that previously existed in a file opened in Append mode.";
        internal const string IO_SetLengthAppendTruncate = "Unable to truncate data that previously existed in a file opened in Append mode.";
        internal const string IO_SourceDestMustBeDifferent = "Source and destination path must be different.";
        internal const string IO_TooManySymbolicLinkLevels = "Too many levels of symbolic links in '{0}'.";
        internal const string IO_UnknownFileName = "[Unknown]";
        internal const string PlatformNotSupported_FileEncryption = "File encryption is not supported on this platform.";
        internal const string PlatformNotSupported_OSXFileLocking = "Locking/unlocking file regions is not supported on this platform. Use FileShare on the entire file instead.";
        internal const string PlatformNotSupported_UnixFileMode = "Unix file modes are not supported on this platform.";
        internal const string UnauthorizedAccess_IODenied_Path = "Access to the path '{0}' is denied.";
    }
}
