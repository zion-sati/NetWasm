using System;
using System.Collections.Concurrent;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Loops;

internal interface IArrayLoopMethodResolver
{
    // An ArrayLoops helper by name, such as Fill, Copy, Xor32 or Sum64, for
    // elements of the type.
    MethodInstanceModel Resolve(string name, CliTypeIdentity element);
}

internal sealed class ArrayLoopMethodResolver(
    ITypeFinder types,
    IMethodRepository methods,
    IMethodInstanceResolver instances,
    ISymbolFormatter symbols) : IArrayLoopMethodResolver
{
    private readonly Lazy<TypeDefinitionModel> _type = new(() => types.FindType("System.Runtime.CompilerServices.ArrayLoops"));
    private readonly ConcurrentDictionary<(string Name, string Element), MethodInstanceModel> _instances = new();

    public MethodInstanceModel Resolve(string name, CliTypeIdentity element) =>
        _instances.GetOrAdd((name, element.CanonicalName), _ =>
        {
            var method = _type.Value.Methods.Select(methods.GetMethod).Single(method => method.Name == name);
            return instances.ResolveMethodInstance(method.Key.Assembly, method.Key.MetadataToken, symbols.Format(method), 0,
                new CliGenericContext([], [element]));
        });
}
