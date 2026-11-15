// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class DiagnosticsPortableContractCompilationTests
{
    public static TheoryData<bool, WasmTarget> Configurations => new()
    {
        { false, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void CompilerExecutesPortableDiagnosticsContracts(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var source = (optimize ? string.Empty : "#define DEBUG\n") + """
            using System;
            using System.Diagnostics;

            namespace DiagnosticsPortableFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var result = 0;

            #if DEBUG
                    Debug.Assert(true, "retained true assertion");
                    Debug.Assert(true, "retained true assertion", "detail");
                    result |= 1;

                    try
                    {
                        Debug.Assert(false, "assertion");
                    }
                    catch (InvalidOperationException exception)
                    {
                        if (exception.Message == "assertion")
                        {
                            result |= 2;
                        }
                    }

                    try
                    {
                        Debug.Assert(false, null, "detail");
                    }
                    catch (InvalidOperationException exception)
                    {
                        if (exception.Message == "detail")
                        {
                            result |= 4;
                        }
                    }

                    try
                    {
                        Debug.Assert(false, "assertion", null);
                    }
                    catch (InvalidOperationException exception)
                    {
                        if (exception.Message == "assertion")
                        {
                            result |= 8;
                        }
                    }

                    try
                    {
                        Debug.Fail("failure");
                    }
                    catch (InvalidOperationException exception)
                    {
                        if (exception.Message == "failure")
                        {
                            result |= 16;
                        }
                    }

                    try
                    {
                        Debug.Fail("failure", "detail");
                    }
                    catch (InvalidOperationException exception)
                    {
                        if (exception.Message == "failure detail")
                        {
                            result |= 32;
                        }
                    }
            #else
                    Debug.Assert(false, "removed assertion");
                    Debug.Fail("removed failure");
                    result |= 63;
            #endif

                    Action action = Marker;
                    if (DiagnosticMethodInfo.Create(action) is null)
                    {
                        result |= 64;
                    }

                    try
                    {
                        DiagnosticMethodInfo.Create(null!);
                    }
                    catch (ArgumentNullException)
                    {
                        result |= 128;
                    }

                    return result + input;
                }

                private static void Marker()
                {
                }
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var observed = executor.Execute(new CompilationScenario(
            optimize ? "DiagnosticsPortableOptimized" : "DiagnosticsPortableDebug",
            source,
            "DiagnosticsPortableFixture.EntryPoint",
            optimize,
            target,
            5,
            []));

        Assert.Equal(260, observed);
    }
}
