// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Core;

// The CoreLib class of every Type object the compiler lays out or the runtime creates: RuntimeType,
// which derives from TypeInfo as .NET's does, so a type's Type object is also its TypeInfo.
public static class RuntimeTypeObjects
{
    public const string ClassName = "System.RuntimeType";
}
