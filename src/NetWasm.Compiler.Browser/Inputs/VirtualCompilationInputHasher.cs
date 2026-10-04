using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NetWasm.Compiler.ExceptionTypes;

namespace NetWasm.Compiler.Browser.Inputs;

internal sealed class VirtualCompilationInputHasher : ICompilationInputHasher
{
    private readonly ImmutableDictionary<string, ImmutableArray<byte>>? _fixedInputs;
    private readonly ImmutableDictionary<string, string>? _fixedCoreBindings;
    private readonly IBrowserCompilationRequestResolver? _requests;

    internal VirtualCompilationInputHasher(
        ImmutableDictionary<string, ImmutableArray<byte>> inputs) =>
        _fixedInputs = inputs ?? throw new ArgumentNullException(nameof(inputs));

    internal VirtualCompilationInputHasher(
        ImmutableDictionary<string, ImmutableArray<byte>> inputs,
        ImmutableDictionary<string, string> coreBindings)
    {
        _fixedInputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
        _fixedCoreBindings = coreBindings ??
            throw new ArgumentNullException(nameof(coreBindings));
    }

    public VirtualCompilationInputHasher(IBrowserCompilationRequestResolver requests) =>
        _requests = requests ?? throw new ArgumentNullException(nameof(requests));

    public string Hash(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var request = _requests?.Resolve();
        var inputs = request?.Inputs ?? _fixedInputs!;
        if (!inputs.TryGetValue(path, out var bytes))
            throw new FileNotFoundException("A semantic virtual compiler input was not supplied.", path);
        var coreBindings = request?.WitCoreBindingInventories ??
            _fixedCoreBindings ?? ImmutableDictionary<string, string>.Empty;
        if (!coreBindings.TryGetValue(path, out var inventory))
        {
            return Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan()));
        }

        var inventoryBytes = Encoding.UTF8.GetBytes(inventory);
        Span<byte> length = stackalloc byte[sizeof(int)];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("NetWasm browser WIT core bindings v1\0"u8);
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes.AsSpan());
        BinaryPrimitives.WriteInt32LittleEndian(length, inventoryBytes.Length);
        hash.AppendData(length);
        hash.AppendData(inventoryBytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
