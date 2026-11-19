// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.DynamicCasts;

internal sealed class DynamicCastHelperResolver(
    ITypeFinder types,
    IMethodRepository methods,
    IMethodInstanceResolver instances,
    ISymbolFormatter symbols) : IDynamicCastHelperResolver
{
    internal const string HelpersType = "System.Runtime.InteropServices.DynamicInterfaceCastableHelpers";

    private readonly ITypeFinder _types = types ?? throw new ArgumentNullException(nameof(types));
    private readonly IMethodRepository _methods = methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly IMethodInstanceResolver _instances = instances ?? throw new ArgumentNullException(nameof(instances));
    private readonly ISymbolFormatter _symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));
    private readonly ConcurrentDictionary<string, MethodInstanceModel> _resolved = new(StringComparer.Ordinal);

    public MethodInstanceModel Resolve(string name, ImmutableArray<CliTypeIdentity> methodArguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return _resolved.GetOrAdd($"{name}<{string.Join(',', methodArguments.Select(type => type.CanonicalName))}>", _ =>
        {
            var definition = _types.FindType(HelpersType).Methods.Select(_methods.GetMethod)
                .Single(method => method.Name == name);
            return _instances.ResolveMethodInstance(definition.Key.Assembly, definition.Key.MetadataToken,
                _symbols.Format(definition), 0, new CliGenericContext([], methodArguments));
        });
    }
}
