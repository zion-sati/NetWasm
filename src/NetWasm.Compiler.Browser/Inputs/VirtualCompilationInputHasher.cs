using System;
using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using NetWasm.Compiler.ExceptionTypes;

namespace NetWasm.Compiler.Browser.Inputs;

internal sealed class VirtualCompilationInputHasher(
    ImmutableDictionary<string, ImmutableArray<byte>> inputs) : ICompilationInputHasher
{
    private readonly ImmutableDictionary<string, ImmutableArray<byte>> _inputs =
        inputs ?? throw new ArgumentNullException(nameof(inputs));

    public string Hash(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!_inputs.TryGetValue(path, out var bytes))
        {
            throw new FileNotFoundException("A semantic virtual compiler input was not supplied.", path);
        }

        return Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan()));
    }
}
