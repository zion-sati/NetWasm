// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ILinkedCorpusObservationResponseReader
{
    LinkedCorpusObservationResult Read(
        string path,
        LinkedCorpusObservationRequest request,
        ImmutableDictionary<int, string> typeNames);
}

internal sealed class LinkedCorpusObservationResponseReader(
    ILinkedCorpusObservationResponseParser parser) : ILinkedCorpusObservationResponseReader
{
    public LinkedCorpusObservationResult Read(
        string path,
        LinkedCorpusObservationRequest request,
        ImmutableDictionary<int, string> typeNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return parser.Parse(File.ReadAllText(path), request, typeNames);
    }
}
