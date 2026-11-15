// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Execution;

internal interface IExecutionContractDefinition
{
    string Key { get; }

    IApplicationExecutionStrategy Strategy { get; }

    void Validate(DeploymentManifest manifest);
}
