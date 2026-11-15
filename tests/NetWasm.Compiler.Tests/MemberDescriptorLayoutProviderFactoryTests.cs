// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Tests;

public sealed class MemberDescriptorLayoutProviderFactoryTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CreatesTheNarrowCapabilityForTheSuppliedSnapshot(WasmTarget target)
    {
        var factory = new MemberDescriptorLayoutProviderFactory();
        var snapshot = MemberDescriptorLayoutProviderTests.Snapshot(target,
            new ManagedStaticData(256, [], [], [], [], [], [], [], [], [])
            {
                MemberDescriptorDeclaringTypeIdOffset = 12,
                MemberDescriptorRequiresDeclaringTypeOffset = 16,
            });

        var provider = ((IMemberDescriptorLayoutProviderFactory)factory).Create(snapshot);

        Assert.IsType<MemberDescriptorLayoutProvider>(provider);
        Assert.Equal(12, provider.DeclaringTypeIdOffset);
        Assert.Equal(16, provider.RequiresDeclaringTypeOffset);
        Assert.IsNotAssignableFrom<IStaticDataLayout>(provider);
    }
}
