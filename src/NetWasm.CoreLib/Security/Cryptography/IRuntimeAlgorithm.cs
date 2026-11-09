// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.

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
