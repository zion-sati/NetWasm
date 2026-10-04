using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.ManagedExecutables;

public sealed class NetWasmHostComponentShimWriter : INetWasmHostComponentShimWriter
{
    private readonly ImmutableDictionary<ManagedExecutableCompletionShape,
        INetWasmHostComponentShimWriter> _writers;

    public NetWasmHostComponentShimWriter(
        IEnumerable<KeyValuePair<ManagedExecutableCompletionShape,
            INetWasmHostComponentShimWriter>> writers)
    {
        ArgumentNullException.ThrowIfNull(writers);
        var registry = ImmutableDictionary.CreateBuilder<ManagedExecutableCompletionShape,
            INetWasmHostComponentShimWriter>();
        foreach (var (shape, writer) in writers)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (!Enum.IsDefined(shape) || !registry.TryAdd(shape, writer))
            {
                throw new ArgumentException("Adapter registrations must have unique, supported completion shapes.", nameof(writers));
            }
        }
        if (registry.Count != Enum.GetValues<ManagedExecutableCompletionShape>().Length)
        {
            throw new ArgumentException("Every managed completion shape requires an adapter.", nameof(writers));
        }
        _writers = registry.ToImmutable();
    }

    public void Write(NetWasmHostComponentShimRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_writers.TryGetValue(request.CompletionShape, out var writer))
        {
            throw new ArgumentOutOfRangeException(nameof(request),
                request.CompletionShape, "Unsupported managed completion shape.");
        }
        writer.Write(request);
    }
}
