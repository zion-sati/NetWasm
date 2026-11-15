// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class RuntimeInteropCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void RuntimeInteropContractsPreserveSupportedExceptionAndMetadataBehavior(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"RuntimeInteropContracts_{optimize}_{target}",
            """
            using System;
            using System.IO;
            using System.Runtime.InteropServices;
            using System.Runtime.Versioning;

            namespace RuntimeInteropContracts;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var passed = 0;
                    passed += Throws<IOException>(unchecked((int)0x80131620));
                    passed += Throws<EndOfStreamException>(unchecked((int)0x80070026));
                    passed += Throws<FileNotFoundException>(unchecked((int)0x80070002));
                    passed += Throws<DirectoryNotFoundException>(unchecked((int)0x80070003));
                    passed += Throws<FileLoadException>(unchecked((int)0x80131621));
                    passed += Throws<PathTooLongException>(unchecked((int)0x800700CE));

                    const int customError = unchecked((int)0x81234567);
                    var external = new ExternalException("failure", customError);
                    if (external.ErrorCode == customError && external.HResult == customError)
                    {
                        passed++;
                    }

                    try
                    {
                        _ = new TargetFrameworkAttribute(null!);
                    }
                    catch (ArgumentNullException)
                    {
                        passed++;
                    }

                    if (Marshal.SystemDefaultCharSize == 2 &&
                        Marshal.SystemMaxDBCSCharSize == 3)
                    {
                        passed++;
                    }

                    return passed == 9 ? 42 : passed;
                }

                private static int Throws<TException>(int hResult)
                    where TException : Exception
                {
                    try
                    {
                        Marshal.ThrowExceptionForHR(hResult);
                    }
                    catch (TException exception)
                    {
                        return exception.HResult == hResult ? 1 : -100;
                    }

                    return -1000;
                }
            }
            """,
            "RuntimeInteropContracts.EntryPoint",
            optimize,
            target,
            7,
            []));

        Assert.Equal(42, result);
    }
}
