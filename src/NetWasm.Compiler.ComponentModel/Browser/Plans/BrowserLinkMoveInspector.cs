// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

public sealed class BrowserLinkMoveInspector : IBrowserLinkOperationInspector
{
    public BrowserLinkOperationContract Inspect(BrowserLinkOperation operation)
    {
        if (operation is not BrowserLinkMoveOperation move)
            throw new ArgumentException("A move operation is required.", nameof(operation));
        if (string.Equals(move.InputPath, move.OutputPath, StringComparison.Ordinal))
            throw new ArgumentException("A move requires distinct source and destination paths.", nameof(operation));
        return new("move", [move.InputPath], [move.OutputPath], [move.InputPath], PreservesValidation: true);
    }
}
