// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Analysis;

// The type facts a program reads at run time, which decide the types those reads can return: any read (the type table
// then holds base types), generic arguments, declaring types, generic definitions, element types and interfaces.
internal readonly record struct RuntimeTypeFactReads(
    bool Any,
    bool GenericArguments = false,
    bool DeclaringTypes = false,
    bool GenericDefinitions = false,
    bool ElementTypes = false,
    bool Interfaces = false);
