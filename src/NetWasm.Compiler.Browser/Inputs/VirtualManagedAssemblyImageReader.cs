using System;
using System.Collections.Immutable;
using System.IO;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Browser.Inputs;

internal sealed class VirtualManagedAssemblyImageReader(
    ImmutableDictionary<string, ImmutableArray<byte>> inputs) : IManagedAssemblyImageReader
{
    private readonly ImmutableDictionary<string, ImmutableArray<byte>> _inputs =
        inputs ?? throw new ArgumentNullException(nameof(inputs));

    public byte[] Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _inputs.TryGetValue(path, out var bytes)
            ? bytes.AsSpan().ToArray()
            : throw new FileNotFoundException("A virtual managed assembly input was not supplied.", path);
    }
}
