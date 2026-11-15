// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

internal sealed class BrowserLinkMoveCapture(IBrowserLinkOperationRecorder recorder) : IFileMover
{
    private readonly IBrowserLinkOperationRecorder _recorder = recorder ?? throw new System.ArgumentNullException(nameof(recorder));
    public void Move(string sourcePath, string destinationPath) => _recorder.Record(new BrowserLinkMoveOperation(sourcePath, destinationPath));
}
