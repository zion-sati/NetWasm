// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeNativeLinkWorkspaceFactory
{
    IRuntimeNativeLinkWorkspace Create(string logDirectory);
}

internal interface IRuntimeNativeLinkWorkspace : IDisposable
{
    string DirectoryPath { get; }
    string LogDirectoryPath { get; }
}
