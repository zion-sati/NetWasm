// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.Diagnostics;

// Returns the collector's committed memory as the analysis, the layouts and the root maps finish, before the stage is
// reported, so the next stage starts from what the compiler still holds and the stage's memory entry shows it.
internal sealed class MemoryReleasingProgressReporter(
    ICompilerProgressReporter inner,
    ICompilerMemoryReleaser releaser) : ICompilerProgressReporter
{
    private readonly ICompilerProgressReporter _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly ICompilerMemoryReleaser _releaser = releaser ?? throw new ArgumentNullException(nameof(releaser));

    public void Report(CompilerProgressStage stage)
    {
        if (stage is CompilerProgressStage.Analysis or CompilerProgressStage.Layouts or CompilerProgressStage.RootMaps)
        {
            _releaser.Release();
        }
        _inner.Report(stage);
    }
}
