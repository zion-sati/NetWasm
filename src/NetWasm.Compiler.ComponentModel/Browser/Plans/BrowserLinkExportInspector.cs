// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

public sealed class BrowserLinkExportInspector : IBrowserLinkOperationInspector
{
    public BrowserLinkOperationContract Inspect(BrowserLinkOperation operation)
    {
        if (operation is not BrowserLinkExportOperation exports)
            throw new ArgumentException("An export operation is required.", nameof(operation));
        if (exports.RemovedExports.IsDefault ||
            (exports.ComponentPrefix is not null &&
             (string.IsNullOrWhiteSpace(exports.ComponentPrefix) || !exports.RemovedExports.IsEmpty)))
            throw new ArgumentException("The export selection must be explicit and unambiguous.", nameof(operation));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var export in exports.RemovedExports)
        {
            if (export is null || string.IsNullOrWhiteSpace(export.Name) ||
                export.Kind > 4 || !seen.Add(export.Name))
                throw new ArgumentException("The export removal identity is invalid or repeated.", nameof(operation));
        }
        return new("exports", [exports.InputPath], [exports.OutputPath], []);
    }
}
