// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

internal sealed class BrowserLinkValidationCapture(IBrowserLinkOperationRecorder recorder) : IComponentPackageOperationRunner
{
    private readonly IBrowserLinkOperationRecorder _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
    public void Run(IEnumerable<string> arguments, string operation)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        var command = arguments.ToImmutableArray();
        if (command.Length != 4)
            throw new ArgumentException("A core validation command requires explicit arguments.", nameof(arguments));
        _recorder.Record(new BrowserLinkValidationOperation(command[1], command));
    }
}
