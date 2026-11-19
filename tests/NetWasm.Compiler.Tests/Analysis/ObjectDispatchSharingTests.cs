// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tests.Analysis;

public sealed class ObjectDispatchSharingTests
{
    // The size presets share at 32 targets, the speed presets at 1,024.
    [Theory]
    [InlineData(ObjectDispatchPolicy.Shared, 31, false)]
    [InlineData(ObjectDispatchPolicy.Shared, 32, true)]
    [InlineData(ObjectDispatchPolicy.SharedAtLargeSites, 1023, false)]
    [InlineData(ObjectDispatchPolicy.SharedAtLargeSites, 1024, true)]
    public void SharesFromThePolicysThreshold(ObjectDispatchPolicy policy, int targets, bool shares) =>
        Assert.Equal(shares, new ObjectDispatchSharing(policy).Shares(targets));

    [Fact]
    public void RejectsAnUnknownPolicyAndANegativeCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ObjectDispatchSharing((ObjectDispatchPolicy)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ObjectDispatchSharing(ObjectDispatchPolicy.Shared).Shares(-1));
    }
}
