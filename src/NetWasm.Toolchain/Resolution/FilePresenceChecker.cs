// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.IO;

namespace NetWasm.Toolchain.Resolution;

public sealed class FilePresenceChecker : IFilePresenceChecker
{
    public bool Exists(string path) => File.Exists(path);
}
