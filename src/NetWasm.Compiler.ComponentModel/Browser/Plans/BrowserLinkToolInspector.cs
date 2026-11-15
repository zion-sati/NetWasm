// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

public sealed class BrowserLinkToolInspector : IBrowserLinkOperationInspector
{
    private readonly FrozenDictionary<string, IBrowserLinkOperationInspector> _tools;

    public BrowserLinkToolInspector(IEnumerable<KeyValuePair<string, IBrowserLinkOperationInspector>> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var registered = new Dictionary<string, IBrowserLinkOperationInspector>(StringComparer.Ordinal);
        foreach (var (name, inspector) in tools)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(inspector);
            if (!registered.TryAdd(name, inspector))
                throw new ArgumentException("A browser tool inspector is registered more than once.", nameof(tools));
        }
        _tools = registered.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public BrowserLinkOperationContract Inspect(BrowserLinkOperation operation)
    {
        if (operation is not BrowserLinkToolOperation tool || string.IsNullOrWhiteSpace(tool.ToolId))
            throw new ArgumentException("A named tool operation is required.", nameof(operation));
        if (!_tools.TryGetValue(tool.ToolId, out var inspector))
            throw new InvalidOperationException("The browser link tool is not supported.");
        return inspector.Inspect(tool);
    }
}
