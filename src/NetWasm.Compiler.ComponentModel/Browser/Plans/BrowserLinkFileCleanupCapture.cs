// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

internal sealed class BrowserLinkFileCleanupCapture(ImmutableHashSet<string> ownedPaths) : IFileDeleter
{
    private readonly ImmutableHashSet<string> _ownedPaths = ownedPaths ?? throw new ArgumentNullException(nameof(ownedPaths));
    public void Delete(string path)
    {
        if (!_ownedPaths.Contains(path))
            throw new InvalidOperationException("The link producer requested cleanup of an unowned artifact.");
    }
}
