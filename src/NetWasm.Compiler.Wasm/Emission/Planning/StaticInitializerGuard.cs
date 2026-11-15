// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed record StaticInitializerGuard(
    int Address,
    EntityKey? Direct,
    string? Constructed)
{
    public OptionalFunctionIndex FunctionIndex { get; init; }

    public static string KeyFor(EntityKey initializer) =>
        $"{initializer.Assembly.Name}:0x{initializer.MetadataToken:x8}";
}
