// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Build.Framework;

namespace NetWasm.Sdk.Pack.MsBuild;

public interface IProjectReferenceAdapter
{
    CanonicalPackageDependencyInput? Adapt(ITaskItem item, string canonicalTargetFramework);
}
