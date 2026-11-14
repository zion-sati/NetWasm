// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Diagnostics;
using System.Formats.Asn1;
using System.Numerics;

namespace System.Security.Cryptography
{
    // Marker interface that goes on runtime-provided algorithms.
    internal interface IRuntimeAlgorithm
    {
    }
}
