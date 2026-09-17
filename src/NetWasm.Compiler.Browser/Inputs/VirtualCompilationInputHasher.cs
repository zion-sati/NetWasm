using System;
using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using NetWasm.Compiler.ExceptionTypes;

namespace NetWasm.Compiler.Browser.Inputs;

internal sealed class VirtualCompilationInputHasher : ICompilationInputHasher
{
    private readonly ImmutableDictionary<string, ImmutableArray<byte>>? _fixedInputs;
    private readonly IBrowserCompilationRequestResolver? _requests;

    internal VirtualCompilationInputHasher(
        ImmutableDictionary<string, ImmutableArray<byte>> inputs) =>
        _fixedInputs = inputs ?? throw new ArgumentNullException(nameof(inputs));

    public VirtualCompilationInputHasher(IBrowserCompilationRequestResolver requests) =>
        _requests = requests ?? throw new ArgumentNullException(nameof(requests));

    public string Hash(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var inputs = _requests?.Resolve().Inputs ?? _fixedInputs!;
        if (!inputs.TryGetValue(path, out var bytes))
            throw new FileNotFoundException("A semantic virtual compiler input was not supplied.", path);
        return Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan()));
    }
}
