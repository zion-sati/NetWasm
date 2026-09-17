using System;
using System.Collections.Immutable;
using System.IO;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Browser.Inputs;

internal sealed class VirtualManagedAssemblyImageReader : IManagedAssemblyImageReader
{
    private readonly ImmutableDictionary<string, ImmutableArray<byte>>? _fixedInputs;
    private readonly IBrowserCompilationRequestResolver? _requests;

    internal VirtualManagedAssemblyImageReader(
        ImmutableDictionary<string, ImmutableArray<byte>> inputs) =>
        _fixedInputs = inputs ?? throw new ArgumentNullException(nameof(inputs));

    public VirtualManagedAssemblyImageReader(IBrowserCompilationRequestResolver requests) =>
        _requests = requests ?? throw new ArgumentNullException(nameof(requests));

    public byte[] Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var inputs = _requests?.Resolve().Inputs ?? _fixedInputs!;
        return inputs.TryGetValue(path, out var bytes)
            ? bytes.AsSpan().ToArray()
            : throw new FileNotFoundException("A virtual managed assembly input was not supplied.", path);
    }
}
