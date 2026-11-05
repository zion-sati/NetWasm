using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Immutable;
using System.IO;
using System.Text;

namespace NetWasm.Compiler.ComponentModel.Raw;

/// <summary>
/// Reads a core module's function imports from its type and import sections, which
/// precede its code, instead of decoding the whole module in a Node process. It
/// applies the inspection command's rules: every import is a function whose
/// signature uses i32, i64, f32 and f64 only, and repeated imports agree. A module
/// with type forms this reader does not model goes to the fallback reader. Module
/// validation belongs to linking, which validates the linked module.
/// </summary>
public sealed class WasmBinaryImportSignatureReader(
    IRawModuleImportSignatureReader fallback) : IRawModuleImportSignatureReader
{
    private const byte TypeSection = 1;
    private const byte ImportSection = 2;
    private const byte FunctionType = 0x60;
    private const byte FunctionImport = 0x00;

    private readonly IRawModuleImportSignatureReader _fallback = fallback ??
        throw new ArgumentNullException(nameof(fallback));

    public ImmutableArray<RawCoreFunctionImportSignature> Read(RawModuleInspectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(request.ModulePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Failure("invalid-bytes");
        }
        return TryRead(bytes, out var imports) ? imports : _fallback.Read(request);
    }

    // False when the module uses a type form this reader does not model.
    internal static bool TryRead(byte[] bytes, out ImmutableArray<RawCoreFunctionImportSignature> imports)
    {
        imports = [];
        var reader = new Cursor(bytes);
        if (bytes.Length < 8 || bytes[0] != 0 || bytes[1] != (byte)'a' || bytes[2] != (byte)'s' ||
            bytes[3] != (byte)'m' || bytes[4] != 1 || bytes[5] != 0 || bytes[6] != 0 || bytes[7] != 0)
        {
            throw Failure("invalid-core-module");
        }
        reader.Position = 8;
        List<FunctionSignature>? types = null;
        while (!reader.AtEnd)
        {
            var id = reader.Byte();
            var size = reader.U32();
            var end = checked(reader.Position + (int)size);
            if (end > bytes.Length)
            {
                throw Failure("invalid-core-module");
            }
            if (id == TypeSection)
            {
                if (!TryReadTypes(reader, out types))
                {
                    return false;
                }
            }
            else if (id == ImportSection)
            {
                imports = ReadImports(reader, types ?? []);
                return true;
            }
            else if (id != 0)
            {
                // Sections after imports: the module imports nothing.
                return true;
            }
            reader.Position = end;
        }
        return true;
    }

    private static bool TryReadTypes(Cursor reader, out List<FunctionSignature> types)
    {
        var count = reader.U32();
        types = new List<FunctionSignature>((int)Math.Min(count, 65536));
        for (var index = 0u; index < count; index++)
        {
            if (reader.Byte() != FunctionType ||
                !TryReadValueTypes(reader, out var parameters) ||
                !TryReadValueTypes(reader, out var results))
            {
                return false;
            }
            types.Add(new(parameters, results));
        }
        return true;
    }

    private static bool TryReadValueTypes(Cursor reader, out ImmutableArray<RawCoreValueType?> values)
    {
        var count = reader.U32();
        var builder = ImmutableArray.CreateBuilder<RawCoreValueType?>((int)Math.Min(count, 1024));
        values = default;
        for (var index = 0u; index < count; index++)
        {
            switch (reader.Byte())
            {
                case 0x7F: builder.Add(RawCoreValueType.I32); break;
                case 0x7E: builder.Add(RawCoreValueType.I64); break;
                case 0x7D: builder.Add(RawCoreValueType.F32); break;
                case 0x7C: builder.Add(RawCoreValueType.F64); break;
                // v128, funcref, externref and exnref: valid, but not in an import signature.
                case 0x7B or 0x70 or 0x6F or 0x69: builder.Add(null); break;
                default: return false;
            }
        }
        values = builder.ToImmutable();
        return true;
    }

    private static ImmutableArray<RawCoreFunctionImportSignature> ReadImports(
        Cursor reader, List<FunctionSignature> types)
    {
        var count = reader.U32();
        var result = ImmutableArray.CreateBuilder<RawCoreFunctionImportSignature>();
        var seen = new Dictionary<RawCanonicalImportIdentity, RawCoreFunctionImportSignature>();
        for (var index = 0u; index < count; index++)
        {
            var identity = new RawCanonicalImportIdentity(reader.Name(), reader.Name());
            if (reader.Byte() != FunctionImport)
            {
                throw Failure("unsupported-import-kind");
            }
            var typeIndex = reader.U32();
            if (typeIndex >= types.Count)
            {
                throw Failure("invalid-core-module");
            }
            var import = new RawCoreFunctionImportSignature(
                identity, Project(types[(int)typeIndex].Parameters), Project(types[(int)typeIndex].Results));
            if (seen.TryGetValue(identity, out var previous))
            {
                if (!previous.Parameters.SequenceEqual(import.Parameters) ||
                    !previous.Results.SequenceEqual(import.Results))
                {
                    throw Failure("inconsistent-import-inventory");
                }
                continue;
            }
            seen.Add(identity, import);
            result.Add(import);
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<RawCoreValueType> Project(ImmutableArray<RawCoreValueType?> values)
    {
        var builder = ImmutableArray.CreateBuilder<RawCoreValueType>(values.Length);
        foreach (var value in values)
        {
            builder.Add(value ?? throw Failure("unsupported-import-signature"));
        }
        return builder.MoveToImmutable();
    }

    private static Compiler.Core.CompilerException Failure(string code) =>
        ComponentException.Tool($"raw module inspection failed with '{code}'");

    private sealed record FunctionSignature(
        ImmutableArray<RawCoreValueType?> Parameters,
        ImmutableArray<RawCoreValueType?> Results);

    private sealed class Cursor(byte[] bytes)
    {
        public int Position { get; set; }

        public bool AtEnd => Position >= bytes.Length;

        public byte Byte() =>
            Position < bytes.Length ? bytes[Position++] : throw Failure("invalid-core-module");

        public uint U32()
        {
            uint value = 0;
            for (var shift = 0; shift < 35; shift += 7)
            {
                var next = Byte();
                value |= (uint)(next & 0x7F) << shift;
                if ((next & 0x80) == 0)
                {
                    return value;
                }
            }
            throw Failure("invalid-core-module");
        }

        public string Name()
        {
            var length = (int)U32();
            if (length > bytes.Length - Position)
            {
                throw Failure("invalid-core-module");
            }
            var value = Encoding.UTF8.GetString(bytes, Position, length);
            Position += length;
            return value;
        }
    }
}
