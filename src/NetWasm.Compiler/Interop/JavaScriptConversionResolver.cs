using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Interop;

internal interface IJavaScriptConversionResolver
{
    MethodInstanceModel Resolve(string name);
}

/// <summary>Finds CoreLib's JavaScript boundary conversions by name (<see cref="JavaScriptConversion"/>).</summary>
internal sealed class JavaScriptConversionResolver(
    ITypeFinder types,
    IMethodRepository methods,
    IMethodInstanceResolver methodInstances,
    ISymbolFormatter symbols) : IJavaScriptConversionResolver
{
    private readonly Dictionary<string, MethodInstanceModel> _conversions = new(StringComparer.Ordinal);
    private ImmutableArray<MethodDefinitionModel> _definitions;

    public MethodInstanceModel Resolve(string name)
    {
        if (_conversions.TryGetValue(name, out var conversion))
        {
            return conversion;
        }
        if (_definitions.IsDefault)
        {
            _definitions = [.. types.FindType(JavaScriptConversion.DeclaringType).Methods.Select(methods.GetMethod)];
        }
        var method = _definitions.Single(candidate => candidate.Name == name);
        conversion = methodInstances.ResolveMethodInstance(
            method.Key.Assembly,
            method.Key.MetadataToken,
            symbols.Format(method),
            0,
            new CliGenericContext(ImmutableArray<CliTypeIdentity>.Empty, []));
        _conversions.Add(name, conversion);
        return conversion;
    }
}
