using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface IOracleModePolicy
{
    OracleMode Mode { get; }

    bool SharesPortableExecutable { get; }

    ImmutableDictionary<string, string> ReferenceAssemblyAliases { get; }

    void Validate(CorpusFixture fixture);

    void ValidateCompilation(CorpusCompilation compilation);
}

internal interface IOracleModePolicyRegistry
{
    IOracleModePolicy Get(OracleMode mode);
}

internal sealed class SameIlOracleModePolicy : IOracleModePolicy
{
    public OracleMode Mode => OracleMode.SameIl;

    public bool SharesPortableExecutable => true;

    public ImmutableDictionary<string, string> ReferenceAssemblyAliases { get; } =
        ImmutableDictionary<string, string>.Empty.Add(
            "System.Runtime", "NetWasm.CoreLib");

    public void Validate(CorpusFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        if (fixture.SameSourceReason is not null)
        {
            throw new InvalidOperationException(
                "same-IL fixture cannot declare a same-source rationale");
        }
    }


    public void ValidateCompilation(CorpusCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        if (!StringComparer.Ordinal.Equals(
                compilation.Desktop.AssemblySha256,
                compilation.NetWasm.AssemblySha256) ||
            !StringComparer.Ordinal.Equals(
                compilation.Desktop.AssemblyPath,
                compilation.NetWasm.AssemblyPath))
        {
            throw new InvalidOperationException(
                "same-IL compilation must use one byte-identical PE on both paths");
        }
    }
}

internal sealed class SameSourceOracleModePolicy : IOracleModePolicy
{
    public OracleMode Mode => OracleMode.SameSource;

    public bool SharesPortableExecutable => false;

    public ImmutableDictionary<string, string> ReferenceAssemblyAliases { get; } =
        ImmutableDictionary<string, string>.Empty;

    public void Validate(CorpusFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        if (string.IsNullOrWhiteSpace(fixture.SameSourceReason))
        {
            throw new InvalidOperationException(
                "same-source fixture must explain why one PE cannot be shared");
        }
    }


    public void ValidateCompilation(CorpusCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
    }
}

internal sealed class FrozenDesktopOracleModePolicy : IOracleModePolicy
{
    public OracleMode Mode => OracleMode.FrozenDesktop;

    public bool SharesPortableExecutable => false;

    public ImmutableDictionary<string, string> ReferenceAssemblyAliases { get; } =
        ImmutableDictionary<string, string>.Empty;

    public void Validate(CorpusFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        if (string.IsNullOrWhiteSpace(fixture.FrozenOracleEvidencePath) ||
            fixture.SameSourceReason is not null)
        {
            throw new InvalidOperationException(
                "frozen desktop fixture must provide independent evidence without a same-source rationale");
        }
    }

    public void ValidateCompilation(CorpusCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
    }
}

internal sealed class OracleModePolicyRegistry(
    IEnumerable<IOracleModePolicy> policies) : IOracleModePolicyRegistry
{
    private readonly ImmutableDictionary<OracleMode, IOracleModePolicy> _policies =
        Create(policies);

    public IOracleModePolicy Get(OracleMode mode) =>
        _policies.TryGetValue(mode, out var policy)
            ? policy
            : throw new InvalidOperationException(
                $"oracle mode '{mode}' has no registered policy");

    private static ImmutableDictionary<OracleMode, IOracleModePolicy> Create(
        IEnumerable<IOracleModePolicy> policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        var result = ImmutableDictionary.CreateBuilder<OracleMode, IOracleModePolicy>();
        foreach (var policy in policies)
        {
            if (!result.TryAdd(policy.Mode, policy))
            {
                throw new InvalidOperationException(
                    $"oracle mode '{policy.Mode}' has duplicate policies");
            }
        }
        return result.ToImmutable();
    }
}
