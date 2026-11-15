// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Hosting.Execution;

/// <summary>An ephemeral host-directory binding and its guest-visible authority.</summary>
public sealed record NetWasmPreopenGrant(
    string HostPath,
    string GuestPath,
    NetWasmPreopenAccess Access);
