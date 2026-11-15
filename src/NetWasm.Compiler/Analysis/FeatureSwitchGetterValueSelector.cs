// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// A Chain of Responsibility link: a feature switch's getter ([FeatureSwitchDefinition(name)] on its property) returns
// the value the project gives the switch; any other method's value is the next link's.
internal sealed class FeatureSwitchGetterValueSelector(
    ImmutableDictionary<string, bool> switches,
    IFeatureSwitchValueSelector next) : IFeatureSwitchValueSelector
{
    private readonly ImmutableDictionary<string, bool> _switches = switches ?? throw new ArgumentNullException(nameof(switches));
    private readonly IFeatureSwitchValueSelector _next = next ?? throw new ArgumentNullException(nameof(next));

    public bool? Select(MethodDefinitionModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return method.FeatureSwitch is { } name && _switches.TryGetValue(name, out var value) ? value : _next.Select(method);
    }
}
