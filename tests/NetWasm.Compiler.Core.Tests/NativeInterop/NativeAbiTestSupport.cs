// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Core.Tests.NativeInterop;

using NetWasm.Compiler.Core.NativeInterop;

internal static class NativeAbiTestSupport
{
    public static INativeAbiSignaturePlanner ScalarSignatures() =>
        new NativeAbiSignaturePlanner(new RejectingAggregates());

    private sealed class RejectingAggregates : INativeAggregateAbiPlanner
    {
        public NativeAbiValuePlan Plan(CliTypeIdentity type, string methodName, NativeMarshallingRules rules) =>
            throw new CompilerException(new(DiagnosticCode.NativeInterop, "Unqualified fixture aggregate.", methodName));
    }
}
