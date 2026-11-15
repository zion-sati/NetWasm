// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// The value of the first of a method's ILLink.Substitutions.xml stubs whose conditions the switches meet (a void
// method's stub selects false, which nothing reads).
internal sealed class FeatureSwitchStubValueSelector(ImmutableDictionary<string, bool> switches) : IFeatureSwitchValueSelector
{
    private readonly ImmutableDictionary<string, bool> _switches = switches ?? throw new ArgumentNullException(nameof(switches));

    public bool? Select(MethodDefinitionModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return method.FeatureSwitchStubs.Select(stub => stub.Select(_switches)).FirstOrDefault(result => result is not null);
    }
}
