// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tests.Correctness;

namespace NetWasm.Compiler.Tests;

// Dispose's GC.SuppressFinalize on an object whose type has no finalizer does nothing, and the runtime skips the
// collector's lookup of it; on an object of a finalizable type, suppressed through its base class's Dispose, it still
// stops the finalizer, while the ones never disposed still run theirs. Compared with desktop .NET.
public sealed class SuppressFinalizeCompatibilityTests(CorrectnessTestRunner runner)
{
    internal const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        namespace SuppressFinalizeFixture;

        public class Resource : IDisposable
        {
            public bool Disposed;

            public void Dispose()
            {
                Disposed = true;
                GC.SuppressFinalize(this);
            }
        }

        public sealed class PlainResource : Resource
        {
        }

        public sealed class FinalizedResource : Resource
        {
            public static int Runs;

            ~FinalizedResource() => Runs++;
        }

        public static class EntryPoint
        {
            // Unreachable once this returns: half disposed (suppressed), half not.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Create(int count)
            {
                var disposed = 0;
                for (var index = 0; index < count; index++)
                {
                    var resource = new FinalizedResource();
                    if ((index & 1) == 0)
                    {
                        resource.Dispose();
                        disposed++;
                    }
                }
                return disposed;
            }

            public static int Run(int input)
            {
                var plain = 0;
                for (var index = 0; index < 1000; index++)
                {
                    using var resource = new PlainResource();
                    plain += resource.Disposed ? 0 : 1;
                }
                if (plain != 1000) return 10;
                var disposed = Create(200);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                if (disposed != 100) return 11;
                if (FinalizedResource.Runs != 100) return 12;
                return input + 1;
            }
        }
        """;

    [Theory]
    [InlineData("Debug-Wasm32-Direct-Linked", FinalWasmOptimization.None)]
    [InlineData("Release-Wasm32-Direct-Linked", FinalWasmOptimization.O3)]
    [InlineData("Release-Wasm32-Direct-Linked", FinalWasmOptimization.Oz)]
    [InlineData("Release-Wasm64-Direct-Linked", FinalWasmOptimization.O3)]
    public void SuppressFinalizeStopsOnlyTheFinalizersItShould(string cell, FinalWasmOptimization optimization)
    {
        var fixture = new CorpusFixture("SuppressFinalizeFixture", "SuppressFinalizeFixture", Source, [41])
        {
            CaseId = "gc.suppress-finalize",
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            ExposesLegacyTrace = false,
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Compare SuppressFinalize on types with and without a finalizer against the desktop runtime.",
            Optimization = optimization,
        };
        runner.Run(fixture);
    }
}
