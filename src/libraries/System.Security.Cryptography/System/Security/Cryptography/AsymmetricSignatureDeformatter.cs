// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Diagnostics;

namespace System.Security.Cryptography
{
    public abstract class AsymmetricSignatureDeformatter
    {
        protected AsymmetricSignatureDeformatter() { }

        public abstract void SetKey(AsymmetricAlgorithm key);
        public abstract void SetHashAlgorithm(string strName);

        public virtual bool VerifySignature(HashAlgorithm hash, byte[] rgbSignature)
        {
            ArgumentNullException.ThrowIfNull(hash);

            SetHashAlgorithm(hash.ToAlgorithmName()!);
            Debug.Assert(hash.Hash != null);
            return VerifySignature(hash.Hash, rgbSignature);
        }

        public abstract bool VerifySignature(byte[] rgbHash, byte[] rgbSignature);
    }
}
