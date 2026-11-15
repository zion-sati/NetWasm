// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

internal sealed class TestTypeRelationshipClassifier(
    bool isAssignable = true,
    int failuresRemaining = 0) : ITypeRelationshipClassifier
{
    private readonly bool _isAssignable = isAssignable;
    private int _failuresRemaining = failuresRemaining;

    public int CallCount { get; private set; }

    public List<(CliTypeIdentity Candidate, CliTypeIdentity Target)> Calls { get; } = [];

    public TypeRelationship Classify(
        CliTypeIdentity candidate,
        CliTypeIdentity target)
    {
        CallCount++;
        Calls.Add((candidate, target));
        if (_failuresRemaining > 0)
        {
            _failuresRemaining--;
            throw new InvalidOperationException("Configured relationship failure.");
        }

        return new((_isAssignable
                ? TypeRelationshipCharacteristics.Hierarchy
                : TypeRelationshipCharacteristics.None));
    }
}
