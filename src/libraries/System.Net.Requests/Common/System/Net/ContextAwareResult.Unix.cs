// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Net
{
    internal sealed partial class ContextAwareResult
    {
        partial void SafeCaptureIdentity();

        partial void CleanupInternal();
    }
}
