// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface ICustomAttributeDescriptorReader
{
    ImmutableArray<CustomAttributeDescriptor> Read(CliTypeIdentity type);
}

// The attributes of one type an assembly declares on itself ([assembly: ...]), found by the attribute type's full
// name; attributes of other types are not resolved, so one whose assembly the compile does not have (a package
// built against System.Runtime) is passed over.
public interface IAssemblyAttributeDescriptorReader
{
    ImmutableArray<CustomAttributeDescriptor> Read(AssemblyIdentity assembly, string attributeFullName);
}
