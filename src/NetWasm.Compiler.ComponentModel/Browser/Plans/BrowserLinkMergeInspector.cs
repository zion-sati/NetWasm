// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ComponentModel.Browser.Plans;

public sealed class BrowserLinkMergeInspector : IBrowserLinkOperationInspector
{
    public BrowserLinkOperationContract Inspect(BrowserLinkOperation operation)
    {
        if (operation is not BrowserLinkToolOperation { ToolId: "wasm-merge" } tool ||
            tool.Arguments.IsDefault || tool.InputPaths.IsDefault || tool.OutputPaths.IsDefault)
            throw new ArgumentException("An explicit merge invocation is required.", nameof(operation));
        var arguments = tool.Arguments;
        var outputIndex = arguments.IndexOf("--output");
        if (outputIndex < 2 || outputIndex % 2 != 0 || outputIndex + 1 >= arguments.Length ||
            tool.OutputPaths.Length != 1 || tool.OutputPaths[0] != arguments[outputIndex + 1])
            throw new ArgumentException("The merge output contract is invalid.", nameof(operation));
        var inputs = ImmutableArray.CreateBuilder<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < outputIndex; index += 2)
        {
            var name = arguments[index + 1];
            if (string.IsNullOrWhiteSpace(name) || name[0] == '-' ||
                name.Any(char.IsControl) || !names.Add(name))
                throw new ArgumentException("The merge module identity is invalid or repeated.", nameof(operation));
            inputs.Add(arguments[index]);
        }
        if (!inputs.SequenceEqual(tool.InputPaths))
            throw new ArgumentException("The merge inputs disagree with its arguments.", nameof(operation));
        var flags = new HashSet<string>(StringComparer.Ordinal);
        for (var index = outputIndex + 2; index < arguments.Length; index++)
        {
            if (!BrowserLinkToolFlags.Features.Contains(arguments[index]) || !flags.Add(arguments[index]))
                throw new ArgumentException("The merge flag is unsupported or repeated.", nameof(operation));
        }
        return new("tool", tool.InputPaths, tool.OutputPaths, [])
        {
            ToolId = tool.ToolId,
            Arguments = tool.Arguments,
            InputArgumentIndices = [.. Enumerable.Range(0, inputs.Count).Select(index => index * 2)],
            OutputArgumentIndices = [outputIndex + 1],
        };
    }
}
