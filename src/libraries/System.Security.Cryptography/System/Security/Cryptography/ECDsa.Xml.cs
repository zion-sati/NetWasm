// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Security.Cryptography
{
    public abstract partial class ECDsa : ECAlgorithm
    {
        // There is currently not a standard XML format for ECC keys, so we will not implement the default
        // To/FromXmlString so that we're not tied to one format when a standard one does exist. Instead we'll
        // use an overload which allows the user to specify the format they'd like to serialize into.
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
