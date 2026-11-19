// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.StaticArrays;

// Whether constructing a value of the type can run no type initializer: a value type with no static constructor, or
// one marked BeforeFieldInit, which construction does not trigger.
internal interface IValueTypeInitializationClassifier
{
    bool ConstructsWithoutInitialization(CliTypeIdentity type);
}
