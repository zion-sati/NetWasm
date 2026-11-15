// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Pipeline;

// Repeats a compile whose functions came out over an engine limit, with nothing inlined into the methods that
// compile to them, while each repeat names a method not yet excluded; then reports the limit as before. A compile
// whose functions all fit runs once, as it always has.
internal sealed class FunctionLimitRecompilingExecutor(ICompilationPipelineExecutor executor) : ICompilationPipelineExecutor
{
    private readonly ICompilationPipelineExecutor _executor = executor ?? throw new ArgumentNullException(nameof(executor));

    public CompilationOutcome Execute(CompilerOptions options, bool serveFromLastCompile = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        while (true)
        {
            try
            {
                return _executor.Execute(options, serveFromLastCompile);
            }
            catch (CompilerException failure) when (Excluding(options.InliningExcludedMethods, failure) is { } excluded)
            {
                options = options with { InliningExcludedMethods = excluded };
            }
        }
    }

    // The exclusions grown by the methods a function limit names, or null when it names none not yet excluded.
    private static ImmutableHashSet<EntityKey>? Excluding(ImmutableHashSet<EntityKey> excluded, CompilerException failure)
    {
        var diagnostic = failure.Diagnostic;
        if (diagnostic.Code != DiagnosticCode.FunctionLimit)
        {
            return null;
        }
        var grown = excluded.Union(diagnostic.LimitExceedingMethods);
        return grown.Count == excluded.Count ? null : grown;
    }
}
