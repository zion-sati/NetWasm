// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

public sealed class BrowserLinkOperationInspector : IBrowserLinkOperationInspector
{
    private readonly FrozenDictionary<Type, IBrowserLinkOperationInspector> _inspectors;

    public BrowserLinkOperationInspector(IEnumerable<KeyValuePair<Type, IBrowserLinkOperationInspector>> inspectors)
    {
        ArgumentNullException.ThrowIfNull(inspectors);
        var registered = new Dictionary<Type, IBrowserLinkOperationInspector>();
        foreach (var (type, inspector) in inspectors)
        {
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(inspector);
            if (!registered.TryAdd(type, inspector))
                throw new ArgumentException("A browser link operation inspector is registered more than once.", nameof(inspectors));
        }
        _inspectors = registered.ToFrozenDictionary();
    }

    public BrowserLinkOperationContract Inspect(BrowserLinkOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!_inspectors.TryGetValue(operation.GetType(), out var inspector))
            throw new InvalidOperationException("The browser link operation is not supported.");
        return inspector.Inspect(operation);
    }
}
