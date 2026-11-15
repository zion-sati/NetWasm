// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

internal static class GeneratedCilTraceVerifier
{
    public static void VerifyManagedReferenceAcrossAllocation(string tracePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tracePath);
        var inEntryPoint = false;
        var storedManagedLocal = false;
        foreach (var line in File.ReadLines(tracePath))
        {
            if (line.StartsWith("[method ", StringComparison.Ordinal))
            {
                inEntryPoint = line.Contains(
                    ".EntryPoint::Run]",
                    StringComparison.Ordinal);
                storedManagedLocal = false;
                continue;
            }
            if (!inEntryPoint)
            {
                continue;
            }
            if (line.Contains(
                    "StoreLocal Index { Value = 1 }",
                    StringComparison.Ordinal))
            {
                storedManagedLocal = true;
                continue;
            }
            if (!storedManagedLocal ||
                !line.Contains(" NewObject ", StringComparison.Ordinal))
            {
                continue;
            }
            if (!line.Contains(
                    "RootSource { Kind = Local, Index = 1",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "generated managed local 1 is not rooted at the next allocation");
            }
            return;
        }
        throw new InvalidDataException(
            "generated diagnostic trace does not contain the managed-local allocation shape");
    }
}
