// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Core;

public enum StackStorageLocationKind
{
    EvaluationStack,
    Local,
}

public readonly record struct StackStorageLocation(StackStorageLocationKind Kind, int Index);
