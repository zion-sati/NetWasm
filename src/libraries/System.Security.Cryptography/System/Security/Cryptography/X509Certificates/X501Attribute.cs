// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Security.Cryptography.X509Certificates
{
    internal class X501Attribute : AsnEncodedData
    {
        internal X501Attribute(string oid, byte[] rawData)
            : base(oid, rawData)
        {
        }

        internal X501Attribute(Oid oid, byte[] rawData)
            : base(oid, rawData)
        {
        }
    }
}
