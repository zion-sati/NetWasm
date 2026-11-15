// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

public sealed class BrowserLinkInlineInspector : IBrowserLinkOperationInspector
{
    public BrowserLinkOperationContract Inspect(BrowserLinkOperation operation)
    {
        if (operation is not BrowserLinkInlineOperation inline)
            throw new ArgumentException("An inline operation is required.", nameof(operation));
        if (!Enum.IsDefined(inline.Selection))
            throw new ArgumentException("The inline selection is not supported.", nameof(operation));
        return new("inline", [inline.InputPath], [inline.OutputPath], [], PreservesValidation: true);
    }
}
