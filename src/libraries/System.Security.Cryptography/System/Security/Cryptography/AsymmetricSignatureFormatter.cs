// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Diagnostics;

namespace System.Security.Cryptography
{
    public abstract class AsymmetricSignatureFormatter
    {
        protected AsymmetricSignatureFormatter() { }

        public abstract void SetKey(AsymmetricAlgorithm key);
        public abstract void SetHashAlgorithm(string strName);

        public virtual byte[] CreateSignature(HashAlgorithm hash)
        {
            ArgumentNullException.ThrowIfNull(hash);

            SetHashAlgorithm(hash.ToAlgorithmName()!);
            Debug.Assert(hash.Hash != null);
            return CreateSignature(hash.Hash);
        }

        public abstract byte[] CreateSignature(byte[] rgbHash);
    }
}
