// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

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
