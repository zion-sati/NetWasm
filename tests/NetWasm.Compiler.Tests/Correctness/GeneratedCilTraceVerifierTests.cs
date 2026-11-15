// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class GeneratedCilTraceVerifierTests
{
    [Fact]
    public void AcceptsManagedLocalPublishedAtFollowingAllocation()
    {
        var path = WriteTrace(
            "IL_0005 StoreLocal Index { Value = 1 }",
            "IL_0007 NewObject Test:0x06000001 roots=[RootSource { Kind = Local, Index = 1, ByteOffset = -1, IsIndirect = False }]");

        GeneratedCilTraceVerifier.VerifyManagedReferenceAcrossAllocation(path);
    }

    [Fact]
    public void RejectsMissingRootAndMissingShape()
    {
        var missingRoot = WriteTrace(
            "IL_0005 StoreLocal Index { Value = 1 }",
            "IL_0007 NewObject Test:0x06000001 roots=[]");
        var missingShape = WriteTrace("IL_0000 Return");

        Assert.Throws<InvalidDataException>(() =>
            GeneratedCilTraceVerifier.VerifyManagedReferenceAcrossAllocation(
                missingRoot));
        Assert.Throws<InvalidDataException>(() =>
            GeneratedCilTraceVerifier.VerifyManagedReferenceAcrossAllocation(
                missingShape));
    }

    private static string WriteTrace(params string[] body)
    {
        var path = Path.Combine(
            CorrectnessTestAssets.CreateDirectory(),
            "diagnostic.trace.txt");
        File.WriteAllLines(path,
        [
            "NETWASM COMPILER DIAGNOSTIC TRACE v1",
            "[method Test.EntryPoint::Run]",
            .. body,
            "",
        ]);
        return path;
    }
}
