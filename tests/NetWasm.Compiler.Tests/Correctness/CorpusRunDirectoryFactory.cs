// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusRunDirectoryFactory
{
    string Create();
}

internal sealed class CorpusRunDirectoryFactory : ICorpusRunDirectoryFactory
{
    public string Create() => Directory.CreateTempSubdirectory("netwasm-corpus-").FullName;
}
