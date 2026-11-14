// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.IO
{
    /// <summary>Provides methods to help in the implementation of Stream-derived types.</summary>
    internal static partial class StreamHelpers
    {
        /// <summary>Validate the arguments to CopyTo, as would Stream.CopyTo.</summary>
        public static void ValidateCopyToArgs(Stream source, Stream destination, int bufferSize)
        {
            ArgumentNullException.ThrowIfNull(destination);

            if (bufferSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bufferSize), bufferSize, "Positive number required.");
            }

            bool sourceCanRead = source.CanRead;
            if (!sourceCanRead && !source.CanWrite)
            {
                throw new ObjectDisposedException(null, "Cannot access a closed stream.");
            }

            bool destinationCanWrite = destination.CanWrite;
            if (!destinationCanWrite && !destination.CanRead)
            {
                throw new ObjectDisposedException(nameof(destination), "Cannot access a closed stream.");
            }

            if (!sourceCanRead)
            {
                throw new NotSupportedException("Stream does not support reading.");
            }

            if (!destinationCanWrite)
            {
                throw new NotSupportedException("Stream does not support writing.");
            }
        }
    }
}
