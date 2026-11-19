// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IAttributeTypeValueClassifier
{
    // Whether a value of this static type is a System.Type (or null).
    bool IsTypeValue(CliTypeIdentity type);
}
