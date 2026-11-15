// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Tests.Execution;

internal static class DescriptorFixture
{
    internal static ExecutionDescriptor Create() => new(
        1,
        new string('a', 64),
        Path.GetFullPath("application/My App.netwasm.deployment.json"),
        new string('b', 64),
        "0.1.0-preview.1",
        Path.GetFullPath("packages/toolchain/node"),
        Path.GetFullPath("packages/hosting/launch.mjs"));
}

internal sealed class DescriptorValidationStub(Action<ExecutionDescriptor> validate) : IExecutionDescriptorValidator
{
    public void Validate(ExecutionDescriptor descriptor) => validate(descriptor);
}
