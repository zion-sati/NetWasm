using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Runtime.Pack.Planning;

internal sealed record RuntimeMaterializationCacheDescriptorRequest(
    string Target,
    RuntimeWasmOptimization Optimization,
    string RuntimeAbi,
    string ToolchainFingerprint,
    long RuntimeGlobalBase,
    long HeapBase,
    long InitialMemorySizeBytes,
    long MaximumMemorySizeBytes,
    ImmutableArray<string> Arguments,
    ImmutableArray<string> OptimizationArguments,
    ImmutableArray<RuntimeLinkPlanAsset> Inputs);

internal sealed class RuntimeMaterializationCacheDescriptorBuilder :
    IRuntimeMaterializationCacheDescriptorBuilder
{
    private const string Schema = "runtime-materialization-cache-v1";
    private static readonly string CacheNamespace = Hash(hash => Append(hash, "schema", Schema));

    public RuntimeMaterializationCacheDescriptor Build(
        RuntimeMaterializationCacheDescriptorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Target);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RuntimeAbi);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ToolchainFingerprint);
        if (!Enum.IsDefined(request.Optimization) ||
            request.Arguments.IsDefault ||
            request.OptimizationArguments.IsDefault ||
            request.Inputs.IsDefault)
        {
            throw new ArgumentException("The runtime cache descriptor request is invalid.", nameof(request));
        }

        var slot = Hash(hash =>
        {
            Append(hash, "schema", Schema);
            Append(hash, "target", request.Target);
            Append(hash, "optimization", request.Optimization.ToString());
        });
        var key = Hash(hash =>
        {
            Append(hash, "schema", Schema);
            Append(hash, "target", request.Target);
            Append(hash, "optimization", request.Optimization.ToString());
            Append(hash, "runtimeAbi", request.RuntimeAbi);
            Append(hash, "toolchainFingerprint", request.ToolchainFingerprint);
            Append(hash, "runtimeGlobalBase", request.RuntimeGlobalBase);
            Append(hash, "heapBase", request.HeapBase);
            Append(hash, "initialMemory", request.InitialMemorySizeBytes);
            Append(hash, "maximumMemory", request.MaximumMemorySizeBytes);
            Append(hash, "arguments.count", request.Arguments.Length);
            for (var index = 0; index < request.Arguments.Length; index++)
            {
                Append(hash, $"arguments.{index}", request.Arguments[index]);
            }
            Append(hash, "optimizationArguments.count", request.OptimizationArguments.Length);
            for (var index = 0; index < request.OptimizationArguments.Length; index++)
            {
                Append(hash, $"optimizationArguments.{index}", request.OptimizationArguments[index]);
            }
            Append(hash, "inputs.count", request.Inputs.Length);
            for (var index = 0; index < request.Inputs.Length; index++)
            {
                Append(hash, $"inputs.{index}.path", request.Inputs[index].Path);
                Append(hash, $"inputs.{index}.sha256", request.Inputs[index].Sha256.ToLowerInvariant());
            }
        });
        return new(Schema, CacheNamespace, slot, key);
    }

    private static string Hash(Action<IncrementalHash> append)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        append(hash);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void Append(IncrementalHash hash, string name, long value) =>
        Append(hash, name, value.ToString(CultureInfo.InvariantCulture));

    private static void Append(IncrementalHash hash, string name, string value)
    {
        AppendBytes(hash, Encoding.UTF8.GetBytes(name));
        AppendBytes(hash, Encoding.UTF8.GetBytes(value));
    }

    private static void AppendBytes(IncrementalHash hash, byte[] value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }
}
