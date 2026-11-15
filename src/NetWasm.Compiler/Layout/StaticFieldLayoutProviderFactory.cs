// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class StaticFieldLayoutProviderFactory : IStaticFieldLayoutProviderFactory
{
    public IStaticFieldLayoutProvider Create(ManagedLayoutSnapshot snapshot) =>
        new StaticFieldLayoutProvider(snapshot);
}
