using System;
using System.Collections.Concurrent;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Loops;

internal interface ISpanLengthResolver
{
    // Span<T>.Length or ReadOnlySpan<T>.Length's getter, for the span type
    // that declares the indexer.
    MethodInstanceModel Resolve(MethodInstanceModel indexer);
}

internal sealed class SpanLengthResolver(
    ITypeFinder types,
    IMethodRepository methods,
    IMethodInstanceResolver instances,
    ISymbolFormatter symbols) : ISpanLengthResolver
{
    private readonly ConcurrentDictionary<string, MethodInstanceModel> _instances = new();

    public MethodInstanceModel Resolve(MethodInstanceModel indexer)
    {
        ArgumentNullException.ThrowIfNull(indexer);
        var span = indexer.DeclaringType;
        return _instances.GetOrAdd(span.CanonicalName, _ =>
        {
            var getter = types.FindType(span.ElementType!.FullName!).Methods.Select(methods.GetMethod)
                .Single(method => method is { Name: "get_Length", IsStatic: false });
            return instances.ResolveMethodInstance(getter.Key.Assembly, getter.Key.MetadataToken, symbols.Format(getter), 0,
                new CliGenericContext(span.TypeArguments, []));
        });
    }
}
