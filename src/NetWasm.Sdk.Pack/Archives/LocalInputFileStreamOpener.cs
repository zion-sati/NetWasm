// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Sdk.Pack.Archives;

public sealed class LocalInputFileStreamOpener : IInputFileStreamOpener
{
    public Stream Open(string sourcePath) => new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
}
