// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Sdk.Pack.Archives;

public sealed class LocalOutputFileStreamOpener : IOutputFileStreamOpener
{
    public Stream Open(string path) => File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
}
