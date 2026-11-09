// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.

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
