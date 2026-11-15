// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Sdk.Pack.Policies;

public sealed class NetWasmPackException : InvalidOperationException
{
    public NetWasmPackException(NetWasmPackErrorCode code, string message)
        : base($"{code}: {message}")
    {
        Code = code;
        SafeMessage = message;
    }

    public NetWasmPackErrorCode Code { get; }

    public string SafeMessage { get; }
}
