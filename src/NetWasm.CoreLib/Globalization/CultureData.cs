// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm runs upstream's invariant globalization mode only, so its one culture data is the
// invariant culture's, with the values dotnet/runtime's CultureData.Invariant sets at
// 474ed963699fc29809015392738a15231aaae2de for the members TextInfo reads.

namespace System.Globalization
{
    internal sealed class CultureData
    {
        internal static CultureData Invariant { get; } = new();

        private CultureData()
        {
        }

        internal string CultureName => "";

        internal string TextInfoName => "";

        internal string ListSeparator => ",";

        internal int ANSICodePage => 1252;

        internal int OEMCodePage => 437;

        internal int MacCodePage => 10000;

        internal int EBCDICCodePage => 037;

        internal bool IsRightToLeft => false;
    }
}
