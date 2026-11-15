// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

internal interface IUnsafeAccessorTypeNameResolver
{
    // The type an UnsafeAccessorTypeAttribute names for an accessor declared in the given assembly, or null
    // when the program has no such type.
    TypeDefinitionModel? Resolve(string typeName, AssemblyIdentity accessorAssembly);
}
