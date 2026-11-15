// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Hosting.Build.Environment;

public interface IHostingBuildEnvironmentResolver
{
    HostingBuildEnvironment Resolve(HostingBuildEnvironmentRequest request);
}

public interface IToolchainPackagePathResolver
{
    ToolchainPackagePaths Resolve(
        string packageRoot,
        NetWasm.Toolchain.Prerequisites.ResolvedHostExecutable node,
        NetWasm.Toolchain.Prerequisites.ValidatedHostToolCompatibility nodeCompatibility);
}
