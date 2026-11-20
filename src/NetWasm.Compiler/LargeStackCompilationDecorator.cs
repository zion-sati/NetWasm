// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler;

// Decorator: a compile runs on a large-stack thread, whoever calls it. Its passes recurse once per nesting level of
// the method they work on, and a caller's own thread, a test's or a host's, has too little stack for a method that
// nests thousands of levels deep, as a long else-if chain does (see LargeStackThreads).
// A host without threads, as a browser is, compiles on the calling thread.
internal sealed class LargeStackCompilationDecorator(INetWasmCompiler compiler, CompilerParallelism parallelism)
    : INetWasmCompiler
{
    private readonly INetWasmCompiler _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
    private readonly CompilerParallelism _parallelism =
        parallelism ?? throw new ArgumentNullException(nameof(parallelism));

    public CompilationResult Compile(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return _parallelism.ThreadsAvailable
            ? LargeStackThreads.Call(() => _compiler.Compile(options))
            : _compiler.Compile(options);
    }
}
