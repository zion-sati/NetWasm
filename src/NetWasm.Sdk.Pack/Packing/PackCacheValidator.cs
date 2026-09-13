using System.Text.Json;
using System.Security.Cryptography;

namespace NetWasm.Sdk.Pack.Packing;

public sealed class PackCacheValidator : IPackCacheValidator
{
    private readonly IPackRequestFingerprintBuilder fingerprintBuilder;

    public PackCacheValidator(IPackRequestFingerprintBuilder fingerprintBuilder)
    {
        this.fingerprintBuilder = fingerprintBuilder ?? throw new ArgumentNullException(nameof(fingerprintBuilder));
    }

    public void Validate(CanonicalPackInputs inputs, CanonicalPackage package)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(package);
        if (!inputs.NoBuild)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(inputs.ManifestOutputPath) || !File.Exists(inputs.ManifestOutputPath))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK004, "No-build packing requires an existing pack manifest.");
        }

        try
        {
            using var stream = File.OpenRead(inputs.ManifestOutputPath);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("RequestHash", out var hash) || string.IsNullOrWhiteSpace(hash.GetString()))
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK004, "The existing pack manifest has no request hash.");
            }

            var currentRequestHash = fingerprintBuilder.Build(package);
            if (!string.Equals(currentRequestHash, hash.GetString(), StringComparison.OrdinalIgnoreCase))
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK004, "The existing pack manifest does not match the current request.");
            }

            if (!document.RootElement.TryGetProperty("PackageHash", out var packageHash) || string.IsNullOrWhiteSpace(packageHash.GetString()) ||
                !File.Exists(inputs.OutputPath) || !string.Equals(packageHash.GetString(), HashFile(inputs.OutputPath), StringComparison.OrdinalIgnoreCase))
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK004, "The existing package output is stale or missing.");
            }

            if (inputs.Symbols is { IncludeSymbols: true })
            {
                if (string.IsNullOrWhiteSpace(inputs.SymbolOutputPath) || !File.Exists(inputs.SymbolOutputPath) ||
                    !document.RootElement.TryGetProperty("SymbolPackageHash", out var symbolHash) || string.IsNullOrWhiteSpace(symbolHash.GetString()) ||
                    !string.Equals(symbolHash.GetString(), HashFile(inputs.SymbolOutputPath), StringComparison.OrdinalIgnoreCase))
                {
                    throw new NetWasmPackException(NetWasmPackErrorCode.NWPK004, "The existing symbol package output is stale or missing.");
                }
            }

            if (!string.IsNullOrWhiteSpace(inputs.ExpectedRequestHash) &&
                !string.Equals(inputs.ExpectedRequestHash, currentRequestHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK004, "The existing pack manifest does not match the current request.");
            }
        }
        catch (NetWasmPackException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK004, "The existing pack manifest is stale or malformed.");
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
