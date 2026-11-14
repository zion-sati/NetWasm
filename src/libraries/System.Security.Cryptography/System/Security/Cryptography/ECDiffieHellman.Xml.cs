// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Security.Cryptography
{
    public abstract partial class ECDiffieHellman : ECAlgorithm
    {
        public override void FromXmlString(string xmlString)
        {
            throw new NotImplementedException(SR.Cryptography_ECXmlSerializationFormatRequired);
        }

        public override string ToXmlString(bool includePrivateParameters)
        {
            throw new NotImplementedException(SR.Cryptography_ECXmlSerializationFormatRequired);
        }
    }
}
