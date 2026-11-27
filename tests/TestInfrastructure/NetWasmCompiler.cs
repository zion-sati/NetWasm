// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler;

// Compiles with a compiler of its own, as each test wants: the CLI, build tasks
// and browser compiler compose theirs from the same registration.
internal static class NetWasmCompiler
{
    public static CompilationResult Compile(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options = options with
        {
            ReferencePaths = [.. NetWasm.TestInfrastructure.TestAssets.WithFrameworkReferences(options.EntryAssemblyPath, options.ReferencePaths)],
            // The dev-loop levels lane (NETWASM_TEST_OPTIMIZATION O0 or None) proves each program reaches the host
            // imports O1 reaches, as the compiler host does for corpus programs.
            VerifyImportParity = options.VerifyImportParity ||
                Environment.GetEnvironmentVariable("NETWASM_TEST_OPTIMIZATION") is "O0" or "None",
        };
        using var services = new ServiceCollection()
            .AddNetWasmCompiler()
            .BuildServiceProvider();
        return services.GetRequiredService<INetWasmCompiler>().Compile(options);
    }
}
