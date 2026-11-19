// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis.StaticArrays;

namespace NetWasm.Compiler.Tests.Analysis.StaticArrays;

using static StaticArrayTestSupport;

public sealed class ValueTypeInitializationClassifierTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void ConstructionRunsNoInitializerUnlessATypeHasAPreciseStaticConstructor(bool initializer, bool beforeFieldInit,
        bool expected)
    {
        var facts = new Facts { HasInitializer = initializer, BeforeFieldInit = beforeFieldInit };
        Assert.Equal(expected, facts.Initialization().ConstructsWithoutInitialization(Element));
        Assert.False(facts.Initialization().ConstructsWithoutInitialization(Holder));
    }

    [Fact]
    public void RejectsMissingDependenciesAndType()
    {
        var facts = new Facts();
        Assert.Throws<ArgumentNullException>(() => new ValueTypeInitializationClassifier(null!, facts));
        Assert.Throws<ArgumentNullException>(() => new ValueTypeInitializationClassifier(facts, null!));
        Assert.Throws<ArgumentNullException>(() => facts.Initialization().ConstructsWithoutInitialization(null!));
    }
}
