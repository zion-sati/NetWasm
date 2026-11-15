// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Tests;

public sealed class ManagedCallSiteValueTests
{
    [Fact]
    public void MethodIdentityHasCanonicalValueSemantics()
    {
        var identity = new ManagedMethodIdentity("Tests.Container.Run()");

        Assert.Equal("Tests.Container.Run()", identity.CanonicalName);
        Assert.Equal(identity, new ManagedMethodIdentity("Tests.Container.Run()"));
        Assert.Equal("Tests.Container.Run()", identity.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MethodIdentityRejectsMissingCanonicalNames(string? canonicalName)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ManagedMethodIdentity(canonicalName!));
    }

    [Fact]
    public void CallSiteKeyHasLocationValueSemantics()
    {
        var caller = new ManagedMethodIdentity("Tests.Container.Run()");
        var key = new ManagedCallSiteKey(caller, 23);

        Assert.Equal(caller, key.Caller);
        Assert.Equal(23, key.InstructionOffset);
        Assert.Equal(key, new ManagedCallSiteKey(caller, 23));
    }

    [Fact]
    public void CallSiteKeyRejectsNegativeOffsets()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ManagedCallSiteKey(new ManagedMethodIdentity("Tests.Container.Run()"), -1));
    }
}
