// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Security.Cryptography.X509Certificates;

namespace System.Security.Cryptography.Asn1
{
    internal partial struct X509ExtensionAsn
    {
        public X509ExtensionAsn(X509Extension extension)
        {
            ArgumentNullException.ThrowIfNull(extension);

            ExtnId = extension.Oid!.Value!;
            Critical = extension.Critical;
            ExtnValue = extension.RawData;
        }
    }
}
