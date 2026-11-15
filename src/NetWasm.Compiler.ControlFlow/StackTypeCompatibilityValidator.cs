// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow;

public sealed class StackTypeCompatibilityValidator : IStackTypeCompatibilityValidator
{
    public bool Accepts(CliValueKind expected, CliValueKind actual) =>
        expected == actual ||
        expected == CliValueKind.NativeInt && actual == CliValueKind.ManagedAddress ||
        expected == CliValueKind.ManagedAddress && actual == CliValueKind.NativeInt;
}
