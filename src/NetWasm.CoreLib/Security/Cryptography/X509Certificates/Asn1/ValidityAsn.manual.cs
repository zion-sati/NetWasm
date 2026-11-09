// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.

namespace System.Security.Cryptography.X509Certificates.Asn1
{
    internal partial struct ValidityAsn
    {
        public ValidityAsn(DateTimeOffset notBefore, DateTimeOffset notAfter)
        {
            NotBefore = new TimeAsn(notBefore);
            NotAfter = new TimeAsn(notAfter);
        }
    }
}
