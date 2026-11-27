// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Merging;

namespace NetWasm.Compiler.ComponentModel.Tests.Merging;

// Debug information keeps names, and NETWASM_KEEP_NAMES asks for them with any value but an empty one.
public sealed class DiagnosticNamesTests
{
    [Theory]
    [InlineData(null, false, false)]
    [InlineData("", false, false)]
    [InlineData("1", false, true)]
    [InlineData("0", false, true)]
    [InlineData(null, true, true)]
    [InlineData("1", true, true)]
    public void DebugInformationOrAnyNonEmptyValueKeepsNames(string? value, bool debugInformation, bool kept) =>
        Assert.Equal(kept, DiagnosticNames.KeptFor(value, debugInformation));

    [Fact]
    public void DebugInformationKeepsNamesWhateverTheEnvironmentSays() => Assert.True(DiagnosticNames.KeptWith(true));
}
