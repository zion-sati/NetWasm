using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface IFrozenOracleEvidenceReader
{
    ImmutableDictionary<int, OracleObservation> Read(FrozenOracleRequest request);
}

internal sealed class FrozenOracleEvidenceReader : IFrozenOracleEvidenceReader
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    public ImmutableDictionary<int, OracleObservation> Read(FrozenOracleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RelativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FixtureName);

        var repositoryRoot = CompilerCorrectnessEnvironment.Discover().RepositoryRoot;
        var path = Path.GetFullPath(Path.Combine(repositoryRoot, request.RelativePath));
        var evidence = JsonSerializer.Deserialize<OracleEvidence>(
            File.ReadAllText(path),
            SerializerOptions)
            ?? throw new InvalidOperationException("oracle evidence is empty");
        ValidateIdentity(evidence, request);
        var hashes = evidence.Hashes
            ?? throw new InvalidOperationException("oracle evidence hashes are missing");

        var profileName = request.Profile.ToString();
        if (evidence.Profiles is null ||
            !evidence.Profiles.TryGetValue(profileName, out var observations) ||
            observations is null)
        {
            throw new InvalidOperationException("oracle evidence profile is missing");
        }
        if (observations.Count != request.Inputs.Length)
        {
            throw new InvalidOperationException("oracle evidence observation count is invalid");
        }
        if (hashes.CanonicalOracleSha256 is null ||
            !hashes.CanonicalOracleSha256.TryGetValue(
                profileName,
                out var expectedOracleHash) ||
            !StringComparer.Ordinal.Equals(
                expectedOracleHash,
                CanonicalHash(observations)))
        {
            throw new InvalidOperationException("oracle evidence canonical hash is invalid");
        }

        var result = ImmutableDictionary.CreateBuilder<int, OracleObservation>();
        foreach (var input in request.Inputs)
        {
            var key = input.ToString(CultureInfo.InvariantCulture);
            if (!observations.TryGetValue(key, out var observation) || observation is null)
            {
                throw new InvalidOperationException("oracle evidence input is missing");
            }
            result.Add(input, observation.ToObservation());
        }
        return result.ToImmutable();
    }

    private static void ValidateIdentity(OracleEvidence evidence, FrozenOracleRequest request)
    {
        if (evidence.SchemaVersion != 2 ||
            !StringComparer.Ordinal.Equals(evidence.Fixture, request.FixtureName) ||
            evidence.RuntimeIdentity is null)
        {
            throw new InvalidOperationException("oracle evidence identity is invalid");
        }

        var runtime = evidence.RuntimeIdentity;
        if (!StringComparer.Ordinal.Equals(runtime.SdkVersion, request.SdkVersion) ||
            !StringComparer.Ordinal.Equals(runtime.TargetFramework, request.TargetFramework) ||
            !StringComparer.Ordinal.Equals(runtime.RuntimeVersion, request.RuntimeVersion) ||
            !StringComparer.Ordinal.Equals(
                runtime.FrameworkDescription,
                request.FrameworkDescription) ||
            !StringComparer.Ordinal.Equals(
                runtime.ProcessArchitecture,
                request.ProcessArchitecture))
        {
            throw new InvalidOperationException("oracle evidence runtime identity is invalid");
        }

        var hashes = evidence.Hashes;
        if (hashes is null)
        {
            return;
        }
        if (!StringComparer.Ordinal.Equals(hashes.SourceSha256, request.SourceSha256) ||
            hashes.DesktopAssemblySha256 is null ||
            !hashes.DesktopAssemblySha256.TryGetValue(
                request.Profile.ToString(),
                out var desktopAssemblyHash) ||
            !StringComparer.Ordinal.Equals(
                desktopAssemblyHash,
                request.DesktopAssemblySha256))
        {
            throw new InvalidOperationException("oracle evidence source or assembly hash is invalid");
        }
    }

    private static string CanonicalHash(
        IReadOnlyDictionary<string, FrozenObservation> observations)
    {
        var canonical = string.Join(
            "\n",
            observations
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => string.Join(
                    "|",
                    pair.Key,
                    pair.Value.Kind,
                    pair.Value.Value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    pair.Value.ExceptionType ?? string.Empty,
                    pair.Value.Trace.ToString(CultureInfo.InvariantCulture))));
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record OracleEvidence(
        int SchemaVersion,
        string? Fixture,
        OracleRuntimeIdentity? RuntimeIdentity,
        OracleHashes? Hashes,
        Dictionary<string, Dictionary<string, FrozenObservation>?>? Profiles);

    private sealed record OracleRuntimeIdentity(
        string? SdkVersion,
        string? TargetFramework,
        string? RuntimeVersion,
        string? FrameworkDescription,
        string? ProcessArchitecture);

    private sealed record OracleHashes(
        string? SourceSha256,
        Dictionary<string, string>? DesktopAssemblySha256,
        Dictionary<string, string>? CanonicalOracleSha256);

    private sealed record FrozenObservation(
        OracleObservationKind Kind,
        int? Value,
        string? ExceptionType,
        int Trace)
    {
        public OracleObservation ToObservation() => new(
            Kind,
            Value,
            ExceptionType,
            Trace);
    }
}
