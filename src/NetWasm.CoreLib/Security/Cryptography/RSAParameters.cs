// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.

using System;
using System.Diagnostics;
using System.Security.Cryptography;

namespace System.Security.Cryptography
{
    [System.Runtime.InteropServices.StructLayoutAttribute(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct RSAParameters
    {
        public byte[]? D;
        public byte[]? DP;
        public byte[]? DQ;
        public byte[]? Exponent;
        public byte[]? InverseQ;
        public byte[]? Modulus;
        public byte[]? P;
        public byte[]? Q;
    }
}
