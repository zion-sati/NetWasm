// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

// One assembly in the program assembly table: its display name, where the program can ask for it, and its embedded
// resources, where the program can read them.
internal sealed record ProgramAssembly(string? DisplayName, ImmutableArray<ManifestResourceData> Resources);

// The program assembly table CoreLib's RuntimeTypeTable reads past the type table's bit stream. Little-endian uint32
// values, every offset from the table's start:
//   count, flags (1: names, 2: resources)
//   per assembly index from 1: name offset, name length, first resource, resource count
//   per resource: name offset, name length, data offset, data length
//   the names (UTF-8), then each resource's bytes at a multiple of eight.
internal static class ProgramAssemblyTableEncoder
{
    internal const int Alignment = 8;
    private const int AssemblyEntryBytes = 16;
    private const int ResourceEntryBytes = 16;

    public static ImmutableArray<byte> Encode(IReadOnlyList<ProgramAssembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        var resources = assemblies.SelectMany(assembly => assembly.Resources).ToArray();
        var names = new List<byte>();
        var entries = new List<(int NameOffset, int NameLength)>();
        var headerBytes = 8 + assemblies.Count * AssemblyEntryBytes + resources.Length * ResourceEntryBytes;

        (int Offset, int Length) Name(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return (0, 0);
            }
            var bytes = Encoding.UTF8.GetBytes(text);
            var offset = headerBytes + names.Count;
            names.AddRange(bytes);
            return (offset, bytes.Length);
        }

        var assemblyNames = assemblies.Select(assembly => Name(assembly.DisplayName)).ToArray();
        var resourceNames = resources.Select(resource => Name(resource.Name)).ToArray();
        var dataOffset = Align(headerBytes + names.Count);
        var resourceData = new int[resources.Length];
        for (var index = 0; index < resources.Length; index++)
        {
            resourceData[index] = dataOffset;
            dataOffset = Align(checked(dataOffset + resources[index].Data.Length));
        }

        var table = new byte[dataOffset];
        var span = table.AsSpan();
        Write(span, 0, assemblies.Count);
        Write(span, 4, (assemblies.Any(assembly => assembly.DisplayName is not null) ? 1 : 0) |
            (resources.Length > 0 ? 2 : 0));
        var firstResource = 0;
        for (var index = 0; index < assemblies.Count; index++)
        {
            var entry = 8 + index * AssemblyEntryBytes;
            Write(span, entry, assemblyNames[index].Offset);
            Write(span, entry + 4, assemblyNames[index].Length);
            Write(span, entry + 8, firstResource);
            Write(span, entry + 12, assemblies[index].Resources.Length);
            firstResource += assemblies[index].Resources.Length;
        }
        for (var index = 0; index < resources.Length; index++)
        {
            var entry = 8 + assemblies.Count * AssemblyEntryBytes + index * ResourceEntryBytes;
            Write(span, entry, resourceNames[index].Offset);
            Write(span, entry + 4, resourceNames[index].Length);
            Write(span, entry + 8, resourceData[index]);
            Write(span, entry + 12, resources[index].Data.Length);
            resources[index].Data.Span.CopyTo(span[resourceData[index]..]);
        }
        names.ToArray().CopyTo(span[headerBytes..]);
        return ImmutableArray.Create(table);
    }

    private static int Align(int offset) => checked((offset + Alignment - 1) / Alignment * Alignment);

    private static void Write(Span<byte> table, int offset, int value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(table[offset..], checked((uint)value));
}
