// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

public sealed class BrowserLinkTextInspector : IBrowserLinkOperationInspector
{
    public BrowserLinkOperationContract Inspect(BrowserLinkOperation operation)
    {
        if (operation is not BrowserLinkTextOperation text)
            throw new ArgumentException("A text operation is required.", nameof(operation));
        ArgumentException.ThrowIfNullOrWhiteSpace(text.Text);
        return new("text", [], [text.OutputPath], []);
    }
}
