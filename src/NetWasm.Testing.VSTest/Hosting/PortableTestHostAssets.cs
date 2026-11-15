// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Testing.VSTest.Hosting;

internal sealed record PortableTestHostAssets(
    string TestHostPath,
    string DependencyManifestPath,
    string RuntimeConfigurationPath,
    string PackageProbingPath);
