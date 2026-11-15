// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Sdk.Pack.References;

public sealed record FrameworkReferenceViewRequest(
    byte[] Implementation,
    byte[] Platform,
    IReadOnlyList<string> ForwardedTypes);

/// <summary>Produces a reference view; never changes the supplied implementation image.</summary>
public interface IFrameworkReferenceViewBuilder
{
    byte[] Build(FrameworkReferenceViewRequest request);
}
