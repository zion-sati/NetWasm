// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NetWasm: internal; it is .NET 10 surface, kept for the PKCS#12 export helpers.

namespace System.Security.Cryptography.X509Certificates
{
    /// <summary>
    /// Specifies the export Password Based Enryption (PBE) parameters with PKCS12 / PFX.
    /// </summary>
    /// <seealso cref="X509Certificate.ExportPkcs12(Pkcs12ExportPbeParameters,string)" />
    internal enum Pkcs12ExportPbeParameters
    {
        /// <summary>
        /// The default parameters.
        /// </summary>
        Default = 0,

        /// <summary>
        /// Uses PBE with Triple-DES and SHA-1.
        /// </summary>
        Pkcs12TripleDesSha1 = 1,

        /// <summary>
        /// Uses PBE with AES-256 and SHA-256.
        /// </summary>
        Pbes2Aes256Sha256 = 2,
    }
}
