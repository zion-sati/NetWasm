// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Tasks.Artifacts;

namespace NetWasm.Compiler.Tasks.MsBuild;

internal interface ICompilerArtifactManifestTaskRequestBuilder
{
    CompilerArtifactManifestBuildRequest Build(CompilerArtifactManifestTaskInput input);
}
