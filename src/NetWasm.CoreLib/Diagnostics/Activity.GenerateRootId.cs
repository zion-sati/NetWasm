// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Diagnostics
{
    partial class Activity
    {
        private static string GenerateRootId()
        {
            // Keep the frequently changing part first, matching the upstream sampling shape.
            return "|" + (++s_currentRootId).ToString("x") + s_uniqSuffix;
        }
    }
}
