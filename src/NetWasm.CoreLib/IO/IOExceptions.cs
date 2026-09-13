// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.IO
{
    public class DirectoryNotFoundException : IOException
    {
        private const int CorEDirectoryNotFound = unchecked((int)0x80070003);

        public DirectoryNotFoundException()
            : this(null, null, null)
        {
        }

        public DirectoryNotFoundException(string? message)
            : this(message, null, null)
        {
        }

        public DirectoryNotFoundException(string? message, Exception? innerException)
            : this(message, null, innerException)
        {
        }

        public DirectoryNotFoundException(string? message, string? directoryPath)
            : this(message, directoryPath, null)
        {
        }

        public DirectoryNotFoundException(
            string? message,
            string? directoryPath,
            Exception? innerException)
            : base(message ?? (directoryPath is null
                ? "Could not find a part of the path."
                : "Could not find a part of the path '" + directoryPath + "'."),
                innerException)
        {
            HResult = CorEDirectoryNotFound;
            DirectoryPath = directoryPath;
        }

        public string? DirectoryPath { get; }

        public override string ToString() => string.IsNullOrEmpty(DirectoryPath)
            ? base.ToString()
            : base.ToString() + " Directory name: '" + DirectoryPath + "'.";
    }

    public class FileLoadException : IOException
    {
        private const int CorEFileLoad = unchecked((int)0x80131621);
        private readonly string? _message;

        public FileLoadException()
            : this(null, null, null)
        {
        }

        public FileLoadException(string? message)
            : this(message, null, null)
        {
        }

        public FileLoadException(string? message, Exception? innerException)
            : this(message, null, innerException)
        {
        }

        public FileLoadException(string? message, string? fileName)
            : this(message, fileName, null)
        {
        }

        public FileLoadException(
            string? message,
            string? fileName,
            Exception? innerException)
            : base(message, innerException)
        {
            HResult = CorEFileLoad;
            _message = message;
            FileName = fileName;
        }

        public override string Message
        {
            get => _message ?? (FileName is null
                ? "Could not load the specified file."
                : "Could not load file '" + FileName + "'.");
        }

        public string? FileName { get; }
        public string? FusionLog
        {
            get => null;
        }

        public override string ToString() => FileName is null
            ? base.ToString()
            : base.ToString() + " File name: '" + FileName + "'.";
    }

    public class FileNotFoundException : IOException
    {
        private const int CorEFileNotFound = unchecked((int)0x80070002);
        private readonly string? _message;

        public FileNotFoundException()
            : this(null, null, null)
        {
        }

        public FileNotFoundException(string? message)
            : this(message, null, null)
        {
        }

        public FileNotFoundException(string? message, Exception? innerException)
            : this(message, null, innerException)
        {
        }

        public FileNotFoundException(string? message, string? fileName)
            : this(message, fileName, null)
        {
        }

        public FileNotFoundException(
            string? message,
            string? fileName,
            Exception? innerException)
            : base(message, innerException)
        {
            HResult = CorEFileNotFound;
            _message = message;
            FileName = fileName;
        }

        public override string Message
        {
            get => _message ?? (FileName is null
                ? "Unable to find the specified file."
                : "Could not find file '" + FileName + "'.");
        }

        public string? FileName { get; }
        public string? FusionLog
        {
            get => null;
        }

        public override string ToString() => FileName is null
            ? base.ToString()
            : base.ToString() + " File name: '" + FileName + "'.";
    }

    public sealed class InvalidDataException : SystemException
    {
        public InvalidDataException()
            : base("Found invalid data while decoding.")
        {
        }

        public InvalidDataException(string? message)
            : base(message ?? "Found invalid data while decoding.")
        {
        }

        public InvalidDataException(string? message, Exception? innerException)
            : base(message ?? "Found invalid data while decoding.", innerException)
        {
        }
    }

    public class PathTooLongException : IOException
    {
        private const int CorEPathTooLong = unchecked((int)0x800700CE);

        public PathTooLongException()
            : this(null, null)
        {
        }

        public PathTooLongException(string? message)
            : this(message, null)
        {
        }

        public PathTooLongException(string? message, Exception? innerException)
            : base(message ?? "The specified path, file name, or both are too long.", innerException)
        {
            HResult = CorEPathTooLong;
        }
    }
}
