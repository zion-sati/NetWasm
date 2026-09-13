using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.ManagedExecutables;

public sealed class ManagedExecutableComponentAdapterWriter : IManagedExecutableComponentAdapterWriter
{
    private readonly ImmutableDictionary<ManagedExecutableCompletionShape,
        IManagedExecutableComponentAdapterWriter> _writers;

    public ManagedExecutableComponentAdapterWriter(
        IEnumerable<KeyValuePair<ManagedExecutableCompletionShape,
            IManagedExecutableComponentAdapterWriter>> writers)
    {
        ArgumentNullException.ThrowIfNull(writers);
        var registry = ImmutableDictionary.CreateBuilder<ManagedExecutableCompletionShape,
            IManagedExecutableComponentAdapterWriter>();
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

    public void Write(ManagedExecutableComponentAdapterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.EntryPoint);
        if (!_writers.TryGetValue(request.EntryPoint.CompletionShape, out var writer))
        {
            throw new ArgumentOutOfRangeException(nameof(request),
                request.EntryPoint.CompletionShape, "Unsupported managed completion shape.");
        }
        writer.Write(request);
    }
}
