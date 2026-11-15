// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Hosting.Execution;

public interface INetWasmExecutionResultValidator
{
    void Validate(NetWasmExecutionResult result);
}
