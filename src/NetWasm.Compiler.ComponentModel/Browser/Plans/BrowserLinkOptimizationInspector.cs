// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

public sealed class BrowserLinkOptimizationInspector : IBrowserLinkOperationInspector
{
    public BrowserLinkOperationContract Inspect(BrowserLinkOperation operation)
    {
        if (operation is not BrowserLinkToolOperation { ToolId: "wasm-opt" } tool ||
            tool.Arguments.IsDefault || tool.InputPaths.IsDefault || tool.OutputPaths.IsDefault)
            throw new ArgumentException("An explicit optimization invocation is required.", nameof(operation));
        var arguments = tool.Arguments;
        if (arguments.Length < 4 || arguments[^2] != "--output" ||
            tool.InputPaths.Length != 1 || tool.InputPaths[0] != arguments[0] ||
            tool.OutputPaths.Length != 1 || tool.OutputPaths[0] != arguments[^1])
            throw new ArgumentException("The optimization artifact contract is invalid.", nameof(operation));
        var flags = new HashSet<string>(StringComparer.Ordinal);
        var profiles = 0;
        for (var index = 1; index < arguments.Length - 2; index++)
        {
            var flag = arguments[index];
            if (!flags.Add(flag))
                throw new ArgumentException("An optimization flag is repeated.", nameof(operation));
            if (BrowserLinkToolFlags.Optimization.Contains(flag))
                profiles++;
            else if (BrowserLinkToolFlags.CountedPasses.Contains(flag))
            {
                if (++index >= arguments.Length - 2 || !IsCount(arguments[index]))
                    throw new ArgumentException("An optimization flag's count is invalid.", nameof(operation));
            }
            else if (!BrowserLinkToolFlags.Features.Contains(flag) && !BrowserLinkToolFlags.Passes.Contains(flag))
                throw new ArgumentException("An optimization flag is not supported.", nameof(operation));
        }
        if (profiles != 1)
            throw new ArgumentException("Exactly one optimization profile must be explicit.", nameof(operation));
        return new("tool", tool.InputPaths, tool.OutputPaths, [])
        {
            ToolId = tool.ToolId,
            Arguments = tool.Arguments,
            InputArgumentIndices = [0],
            OutputArgumentIndices = [arguments.Length - 1],
        };
    }

    // A positive decimal count as the compiler writes it: digits only, no sign or leading zero.
    private static bool IsCount(string value) =>
        value.Length is > 0 and <= 9 && value[0] is >= '1' and <= '9' && !value.AsSpan().ContainsAnyExceptInRange('0', '9');
}
