// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Sdk.Pack.Packing;

/// <summary>
/// Moves one file as part of an output publication operation.
/// </summary>
public interface IFileMover
{
    void Move(string sourcePath, string destinationPath, bool overwrite);
}
