// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace NetWasm.Runtime.Pack.Materialization;

// Moves a linked module's function imports from env to the import module the project declared for each. Emscripten
// archives import their JavaScript functions from env, which is the runtime's own module; a plain core module's
// JavaScript supplies them through an import module of the project's instead. Every other byte is kept.
internal sealed class RuntimeEnvImportRelocator : IRuntimeEnvImportRelocator
{
    private const string EnvModule = "env";
    private const byte ImportSectionId = 2;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public byte[] Relocate(ReadOnlyMemory<byte> module, ImmutableArray<RuntimeEnvImport> imports)
    {
        if (imports.IsDefault)
            throw new ArgumentException("The env imports are required.", nameof(imports));
        var modules = imports.ToDictionary(import => import.Name, import => import.Module, StringComparer.Ordinal);
        if (modules.Count == 0)
            return module.ToArray();
        try
        {
            var source = module.Span;
            if (!WasmImportSection.HasHeader(source))
                throw Invalid();
            using var output = new MemoryStream(source.Length + 64);
            output.Write(source[..WasmImportSection.HeaderLength]);
            var offset = WasmImportSection.HeaderLength;
            while (offset < source.Length)
            {
                var start = offset;
                var id = source[offset++];
                var length = checked((int)WasmImportSection.ReadUnsigned(source, ref offset));
                var end = checked(offset + length);
                if (end > source.Length)
                    throw Invalid();
                if (id != ImportSectionId)
                {
                    output.Write(source[start..end]);
                    offset = end;
                    continue;
                }
                var payload = RelocateImports(source[offset..end], modules);
                output.WriteByte(id);
                WriteUnsigned(output, checked((uint)payload.Length));
                output.Write(payload);
                offset = end;
            }
            return output.ToArray();
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or IndexOutOfRangeException or
            DecoderFallbackException or OverflowException)
        {
            throw Invalid(exception);
        }
    }

    private static byte[] RelocateImports(ReadOnlySpan<byte> section, Dictionary<string, string> modules)
    {
        using var output = new MemoryStream(section.Length + 64);
        var offset = 0;
        var count = WasmImportSection.ReadUnsigned(section, ref offset);
        WriteUnsigned(output, count);
        for (var index = 0u; index < count; index++)
        {
            var moduleName = WasmImportSection.ReadName(section, ref offset);
            var fieldStart = offset;
            var field = WasmImportSection.ReadName(section, ref offset);
            var kind = section[offset];
            var descriptionEnd = WasmImportSection.SkipDescription(section, offset + 1, kind);
            var relocated = kind == 0 && moduleName == EnvModule && modules.TryGetValue(field, out var target)
                ? target
                : moduleName;
            WriteName(output, relocated);
            output.Write(section[fieldStart..descriptionEnd]);
            offset = descriptionEnd;
        }
        if (offset != section.Length)
            throw Invalid();
        return output.ToArray();
    }

    private static void WriteName(Stream output, string value)
    {
        var bytes = Utf8.GetBytes(value);
        WriteUnsigned(output, checked((uint)bytes.Length));
        output.Write(bytes);
    }

    private static void WriteUnsigned(Stream output, uint value)
    {
        while (true)
        {
            var item = (byte)(value & 0x7f);
            value >>= 7;
            output.WriteByte((byte)(item | (value == 0 ? 0 : 0x80)));
            if (value == 0)
                return;
        }
    }

    private static InvalidOperationException Invalid(Exception? cause = null) => WasmImportSection.Invalid(cause);
}
