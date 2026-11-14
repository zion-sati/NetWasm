// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.IO.Compression
{
    /// <summary>
    /// Specifies the encryption method used to encrypt an entry in a zip archive.
    /// </summary>
    public enum ZipEncryptionMethod
    {
        /// <summary>
        /// An encryption method that is not supported by this implementation.
        /// Attempting to open an entry with this encryption method will throw <see cref="NotSupportedException"/>.
        /// </summary>
        Unknown = -1,

        /// <summary>
        /// No Encryption is applied to the entry.
        /// </summary>
        None = 0,

        /// <summary>
        /// Legacy PKware encryption.
        /// </summary>
        ZipCrypto = 1,

        /// <summary>
        /// WinZip AES encryption with 128-bit key.
        /// </summary>
        Aes128 = 2,

        /// <summary>
        /// WinZip AES encryption with 192-bit key.
        /// </summary>
        Aes192 = 3,

        /// <summary>
        /// WinZip AES encryption with 256-bit key.
        /// </summary>
        Aes256 = 4
    }
}
