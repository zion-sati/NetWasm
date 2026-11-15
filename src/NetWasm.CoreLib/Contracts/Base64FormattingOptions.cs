// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// Contract adapted from dotnet/runtime System.Private.CoreLib.
// Licensed under the MIT license; see the upstream repository for the
// complete copyright notice.
namespace System
{
    [Flags]
    public enum Base64FormattingOptions
    {
        None = 0,
        InsertLineBreaks = 1
    }
}
