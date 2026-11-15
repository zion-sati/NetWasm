// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Sdk.Pack.Packing;

/// <summary>
/// Performs output moves using the host filesystem.
/// </summary>
public sealed class FileMover : IFileMover
{
    public void Move(string sourcePath, string destinationPath, bool overwrite) =>
        File.Move(sourcePath, destinationPath, overwrite);
}
