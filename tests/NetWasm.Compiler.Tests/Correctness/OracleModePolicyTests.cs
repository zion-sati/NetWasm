// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class OracleModePolicyTests
{
    [Fact]
    public void RegistryResolvesBothFrozenModes()
    {
        var registry = CorrectnessTestAssets.CreateOracleModes();

        Assert.IsType<SameIlOracleModePolicy>(registry.Get(OracleMode.SameIl));
        Assert.IsType<SameSourceOracleModePolicy>(registry.Get(OracleMode.SameSource));
        Assert.IsType<FrozenDesktopOracleModePolicy>(registry.Get(OracleMode.FrozenDesktop));
    }

    [Fact]
    public void RegistryRejectsDuplicateAndMissingModes()
    {
        Assert.Throws<InvalidOperationException>(() => new OracleModePolicyRegistry(
            [new SameIlOracleModePolicy(), new SameIlOracleModePolicy()]));
        var registry = new OracleModePolicyRegistry([]);
        Assert.Throws<InvalidOperationException>(() => registry.Get(OracleMode.SameIl));
    }

    [Fact]
    public void PoliciesValidateModeSpecificContracts()
    {
        var fixture = CorrectnessTestAssets.CreateFixture();
        Assert.Throws<InvalidOperationException>(() =>
            new SameSourceOracleModePolicy().Validate(
                fixture with { SameSourceReason = null }));
        Assert.Throws<InvalidOperationException>(() =>
            new SameIlOracleModePolicy().Validate(fixture));
        new SameIlOracleModePolicy().Validate(fixture with
        {
            OracleMode = OracleMode.SameIl,
            SameSourceReason = null,
        });
        new FrozenDesktopOracleModePolicy().Validate(fixture with
        {
            OracleMode = OracleMode.FrozenDesktop,
            FrozenOracleEvidencePath = "oracle.json",
            SameSourceReason = null,
        });
    }

    [Fact]
    public void SameIlPolicyRejectsDifferentPathsOrHashes()
    {
        var fixture = CorrectnessTestAssets.CreateFixture() with
        {
            OracleMode = OracleMode.SameIl,
            SameSourceReason = null,
        };
        var artifact = new CorpusArtifact(
            "shared.dll",
            "shared.pdb",
            "assembly-hash",
            "pdb-hash",
            "sdk",
            []);
        var policy = new SameIlOracleModePolicy();

        policy.ValidateCompilation(new(
            fixture,
            CilProfile.Release,
            artifact,
            artifact,
            "output"));
        Assert.Throws<InvalidOperationException>(() => policy.ValidateCompilation(new(
            fixture,
            CilProfile.Release,
            artifact,
            artifact with { AssemblyPath = "recompiled.dll" },
            "output")));
        Assert.Throws<InvalidOperationException>(() => policy.ValidateCompilation(new(
            fixture,
            CilProfile.Release,
            artifact,
            artifact with { AssemblySha256 = "different-hash" },
            "output")));
    }
}
