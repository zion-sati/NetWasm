// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

internal sealed class BrowserLinkDirectoryCleanupCapture(string directory) : IDirectoryDeleter
{
    private readonly string _directory = directory ?? throw new ArgumentNullException(nameof(directory));
    public void Delete(string path)
    {
        if (!string.Equals(path, _directory, StringComparison.Ordinal))
            throw new InvalidOperationException("The link producer requested cleanup of an unowned directory.");
    }
}
