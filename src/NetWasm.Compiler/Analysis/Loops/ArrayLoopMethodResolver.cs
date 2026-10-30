using System;
using System.Collections.Concurrent;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Loops;

internal interface IArrayLoopMethodResolver
{
    // ArrayLoops.Fill<T> or ArrayLoops.Copy<T> for elements of the type.
    MethodInstanceModel Fill(CliTypeIdentity element);

    MethodInstanceModel Copy(CliTypeIdentity element);

    // ArrayLoops.Xor32<T>, Or64<T> and the like, by name.
    MethodInstanceModel Reduction(string name, CliTypeIdentity element);

    // Span<T>.Length or ReadOnlySpan<T>.Length's getter, for the span type
    // that declares the indexer.
    MethodInstanceModel SpanLength(MethodInstanceModel indexer);
}

internal sealed class ArrayLoopMethodResolver(
    ITypeFinder types,
    IMethodRepository methods,
    IMethodInstanceResolver instances,
    ISymbolFormatter symbols) : IArrayLoopMethodResolver
{
    private readonly Lazy<TypeDefinitionModel> _type = new(() => types.FindType("System.Runtime.CompilerServices.ArrayLoops"));
    private readonly ConcurrentDictionary<(string Name, string Element), MethodInstanceModel> _instances = new();

    public MethodInstanceModel Fill(CliTypeIdentity element) => Resolve("Fill", element);

    public MethodInstanceModel Copy(CliTypeIdentity element) => Resolve("Copy", element);

    public MethodInstanceModel Reduction(string name, CliTypeIdentity element) => Resolve(name, element);

    public MethodInstanceModel SpanLength(MethodInstanceModel indexer)
    {
        ArgumentNullException.ThrowIfNull(indexer);
        var span = indexer.DeclaringType;
        return _instances.GetOrAdd(("get_Length", span.CanonicalName), _ =>
        {
            var getter = types.FindType(span.ElementType!.FullName!).Methods.Select(methods.GetMethod)
                .Single(method => method is { Name: "get_Length", IsStatic: false });
            return instances.ResolveMethodInstance(getter.Key.Assembly, getter.Key.MetadataToken, symbols.Format(getter), 0,
                new CliGenericContext(span.TypeArguments, []));
        });
    }

    private MethodInstanceModel Resolve(string name, CliTypeIdentity element) =>
        _instances.GetOrAdd((name, element.CanonicalName), _ =>
        {
            var method = _type.Value.Methods.Select(methods.GetMethod).Single(method => method.Name == name);
            return instances.ResolveMethodInstance(method.Key.Assembly, method.Key.MetadataToken, symbols.Format(method), 0,
                new CliGenericContext([], [element]));
        });
}
