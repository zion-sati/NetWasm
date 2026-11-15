// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace NetWasm.Compiler.Metadata;

/// <summary>Reads the bytes of a resource an assembly embeds, by its name.</summary>
internal interface IEmbeddedResourceReader
{
    // The resource's bytes, or null where the assembly embeds no resource of that name.
    byte[]? Read(PEReader image, MetadataReader reader, string name);
}
