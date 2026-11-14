// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System;
using System.Diagnostics;

namespace System.Security.Cryptography.X509Certificates
{
    /// <summary>
    /// This enum defines the Open modes. Read/ReadWrite/MaxAllowed are mutually exclusive.
    /// </summary>
    [Flags]
    public enum OpenFlags
    {
        ReadOnly = 0x00,
        ReadWrite = 0x01,
        MaxAllowed = 0x02,
        OpenExistingOnly = 0x04,
        IncludeArchived = 0x08,
    }
}
