using System.Globalization;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

using NetWasm.Sdk.Pack.Archives;
using NetWasm.Sdk.Pack.Packing;

using MsBuildTask = Microsoft.Build.Utilities.Task;

namespace NetWasm.Sdk.Pack;

public sealed class DeterministicPackageArchiveTask : MsBuildTask
{
    private readonly IPackageArchiveCanonicalizer canonicalizer;

    public DeterministicPackageArchiveTask()
        : this(PackComposition.CreateArchiveCanonicalizer())
    {
    }

    public DeterministicPackageArchiveTask(IPackageArchiveCanonicalizer canonicalizer)
    {
        this.canonicalizer = canonicalizer ?? throw new ArgumentNullException(nameof(canonicalizer));
    }

    public string PackagePath { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = string.Empty;
    public string? SourceDateEpoch { get; set; }

    public override bool Execute()
    {
        try
        {
            var identity = new PackageIdentity(PackageId, PackageVersion);
            var request = new PackageArchiveNormalizationRequest(
                PackagePath,
                PackagePath,
                identity,
                DeterminismPolicy.Default with { EntryTimestamp = ResolveTimestamp(SourceDateEpoch) });
            canonicalizer.Canonicalize(request);
            Log.LogMessage(MessageImportance.Low, "Canonicalized package archive deterministically.");
            return true;
        }
        catch (Exception exception)
        {
            var code = exception is NetWasmPackException packException ? packException.Code : NetWasmPackErrorCode.NWPK014;
            var message = exception is NetWasmPackException safeException ? safeException.SafeMessage : "The deterministic package archive operation failed.";
            Log.LogError($"{code}: {message}");
            return false;
        }
    }

    private static DateTimeOffset ResolveTimestamp(string? sourceDateEpoch)
    {
        if (string.IsNullOrEmpty(sourceDateEpoch))
        {
            return DeterminismPolicy.Default.EntryTimestamp;
        }

        if (!long.TryParse(sourceDateEpoch, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var epoch))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK013, "The source date epoch must be a whole-number Unix timestamp.");
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(epoch);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK013, "The source date epoch is outside the supported range.");
        }
    }
}
