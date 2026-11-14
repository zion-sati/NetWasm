using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.ModuleEncoding;

public sealed class ModuleEncoder : IModuleEncoder
{
    private static readonly byte[] Header =
        [0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00];

    private readonly IUnsignedLeb128Encoder _unsigned;
    private readonly IUnsignedLeb12864Encoder _unsigned64;
    private readonly ISignedLeb128Encoder _signed;
    private readonly ISignedLeb12864Encoder _signed64;
    private readonly IUtf8StringEncoder _utf8;
    private readonly IWasmSectionWriter _sections;
    private readonly IFunctionInlineAnnotationWriter _inlineAnnotations;
    private readonly Func<WasmBinaryBuffer, IWasmBinaryWriter> _writerFactory;
    private readonly Func<WasmBinaryBuffer, IWasmBinarySnapshotReader>
        _snapshotReaderFactory;
    private readonly Func<IWasmBinaryWriter, IWasmInstructionWriter>
        _instructionWriterFactory;

    public ModuleEncoder(
        IUnsignedLeb128Encoder unsignedEncoder,
        IUnsignedLeb12864Encoder unsigned64Encoder,
        ISignedLeb128Encoder signedEncoder,
        ISignedLeb12864Encoder signed64Encoder,
        IUtf8StringEncoder utf8,
        IWasmSectionWriter sections,
        Func<WasmBinaryBuffer, IWasmBinaryWriter> writerFactory,
        Func<WasmBinaryBuffer, IWasmBinarySnapshotReader> snapshotReaderFactory,
        Func<IWasmBinaryWriter, IWasmInstructionWriter> instructionWriterFactory,
        IFunctionInlineAnnotationWriter inlineAnnotations)
    {
        ArgumentNullException.ThrowIfNull(unsignedEncoder);
        ArgumentNullException.ThrowIfNull(unsigned64Encoder);
        ArgumentNullException.ThrowIfNull(signedEncoder);
        ArgumentNullException.ThrowIfNull(signed64Encoder);
        ArgumentNullException.ThrowIfNull(utf8);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(writerFactory);
        ArgumentNullException.ThrowIfNull(snapshotReaderFactory);
        ArgumentNullException.ThrowIfNull(instructionWriterFactory);
        ArgumentNullException.ThrowIfNull(inlineAnnotations);
        _unsigned = unsignedEncoder;
        _unsigned64 = unsigned64Encoder;
        _signed = signedEncoder;
        _signed64 = signed64Encoder;
        _utf8 = utf8;
        _sections = sections;
        _writerFactory = writerFactory;
        _snapshotReaderFactory = snapshotReaderFactory;
        _instructionWriterFactory = instructionWriterFactory;
        _inlineAnnotations = inlineAnnotations;
    }

    public byte[] Encode(WasmModuleBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.FunctionImports);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MemoryImportModule);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MemoryImportName);
        ArgumentNullException.ThrowIfNull(request.Functions);
        ArgumentNullException.ThrowIfNull(request.Exports);
        ArgumentNullException.ThrowIfNull(request.DataSegments);

        var types = new List<WasmFunctionType>();
        int InternType(WasmFunctionType type)
        {
            ArgumentNullException.ThrowIfNull(type);
            var existing = types.FindIndex(candidate =>
                candidate.Result == type.Result &&
                candidate.Parameters.AsSpan().SequenceEqual(type.Parameters.AsSpan()));
            if (existing >= 0)
            {
                return existing;
            }
            types.Add(type);
            return types.Count - 1;
        }

        var importTypes = request.FunctionImports
            .Select(import => InternType(import.Type))
            .ToArray();
        var functionTypes = request.Functions
            .Select(function => InternType(function.Type))
            .ToArray();
        int? exceptionTagType = request.IncludeManagedExceptionTag
            ? InternType(WasmFunctionType.Create(
                CliValueKind.Void,
                CliValueKind.ManagedReference))
            : null;

        var moduleBuffer = new WasmBinaryBuffer();
        var module = _writerFactory(moduleBuffer);
        module.Write(Header);
        var targetLayout = WasmTargetLayout.For(request.Target);
        WriteTypeSection(module, types, targetLayout);
        WriteImportSection(
            module,
            request.FunctionImports,
            importTypes,
            request.MemoryImportModule,
            request.MemoryImportName,
            targetLayout);
        WriteFunctionSection(module, functionTypes);
        if (exceptionTagType is int tagType)
        {
            WriteTagSection(module, tagType);
        }
        WriteExportSection(module, request.Exports);
        WriteCodeSection(module, request.Functions);
        if (request.DataSegments.Count != 0)
        {
            WriteDataSection(module, request.DataSegments, targetLayout);
        }
        if (request.IncludeNameSection)
        {
            WriteNameSection(module, request.FunctionImports, request.Functions);
        }
        _inlineAnnotations.Write(module, request.Functions, request.FunctionImports.Count);
        return _snapshotReaderFactory(moduleBuffer).Read();
    }

    private void WriteTagSection(IWasmBinaryWriter module, int typeIndex)
    {
        var payloadBuffer = new WasmBinaryBuffer();
        var payload = _writerFactory(payloadBuffer);
        _unsigned.Encode(payload, 1);
        payload.Write([0x00]);
        _unsigned.Encode(payload, (uint)typeIndex);
        _sections.Write(module, 13, _snapshotReaderFactory(payloadBuffer).Read());
    }

    private void WriteTypeSection(
        IWasmBinaryWriter module,
        List<WasmFunctionType> types,
        WasmTargetLayout target)
    {
        var payloadBuffer = new WasmBinaryBuffer();
        var payload = _writerFactory(payloadBuffer);
        _unsigned.Encode(payload, (uint)types.Count);
        foreach (var type in types)
        {
            payload.Write([0x60]);
            _unsigned.Encode(payload, (uint)type.Parameters.Length);
            foreach (var parameter in type.Parameters)
            {
                if (parameter == CliValueKind.Void)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(types),
                        parameter,
                        "Void is not a Wasm value type.");
                }
                payload.Write([(byte)WasmValueTypes.FromCli(parameter, target)]);
            }
            if (type.Result == CliValueKind.Void)
            {
                _unsigned.Encode(payload, 0);
            }
            else
            {
                _unsigned.Encode(payload, 1);
                payload.Write([(byte)WasmValueTypes.FromCli(type.Result, target)]);
            }
        }
        _sections.Write(module, 1, _snapshotReaderFactory(payloadBuffer).Read());
    }

    private void WriteImportSection(
        IWasmBinaryWriter module,
        IReadOnlyList<WasmFunctionImport> imports,
        int[] typeIndices,
        string memoryModule,
        string memoryName,
        WasmTargetLayout target)
    {
        var payloadBuffer = new WasmBinaryBuffer();
        var payload = _writerFactory(payloadBuffer);
        _unsigned.Encode(payload, (uint)imports.Count + 1);
        for (var index = 0; index < imports.Count; index++)
        {
            _utf8.Encode(payload, imports[index].Module);
            _utf8.Encode(payload, imports[index].Name);
            payload.Write([0x00]);
            _unsigned.Encode(payload, (uint)typeIndices[index]);
        }
        _utf8.Encode(payload, memoryModule);
        _utf8.Encode(payload, memoryName);
        payload.Write([0x02, target.UsesMemory64 ? (byte)0x04 : (byte)0x00]);
        if (target.UsesMemory64)
        {
            _unsigned64.Encode(payload, 1);
        }
        else
        {
            _unsigned.Encode(payload, 1);
        }
        _sections.Write(module, 2, _snapshotReaderFactory(payloadBuffer).Read());
    }

    private void WriteFunctionSection(
        IWasmBinaryWriter module,
        int[] typeIndices)
    {
        var payloadBuffer = new WasmBinaryBuffer();
        var payload = _writerFactory(payloadBuffer);
        _unsigned.Encode(payload, (uint)typeIndices.Length);
        foreach (var index in typeIndices)
        {
            _unsigned.Encode(payload, (uint)index);
        }
        _sections.Write(module, 3, _snapshotReaderFactory(payloadBuffer).Read());
    }

    private void WriteExportSection(
        IWasmBinaryWriter module,
        IReadOnlyList<WasmExport> exports)
    {
        var payloadBuffer = new WasmBinaryBuffer();
        var payload = _writerFactory(payloadBuffer);
        _unsigned.Encode(payload, (uint)exports.Count);
        foreach (var export in exports)
        {
            _utf8.Encode(payload, export.Name);
            payload.Write([(byte)export.Kind]);
            _unsigned.Encode(payload, (uint)export.Index);
        }
        _sections.Write(module, 7, _snapshotReaderFactory(payloadBuffer).Read());
    }

    private void WriteCodeSection(
        IWasmBinaryWriter module,
        IReadOnlyList<WasmFunctionDefinition> functions)
    {
        var payloadBuffer = new WasmBinaryBuffer();
        var payload = _writerFactory(payloadBuffer);
        _unsigned.Encode(payload, (uint)functions.Count);
        foreach (var function in functions)
        {
            RequireLoadableFunction(function);
            _unsigned.Encode(payload, (uint)function.Body.Length);
            payload.Write(function.Body);
        }
        _sections.Write(module, 10, _snapshotReaderFactory(payloadBuffer).Read());
    }

    // Engines refuse to compile a module containing a larger function, and the
    // validator after linking can only name its byte offset.
    internal const int MaximumFunctionBodySize = 7_654_321;
    internal const int MaximumFunctionParameters = 1_000;
    // Its parameters count among a function's locals, as V8 and Wasmtime count them.
    internal const int MaximumFunctionLocals = 50_000;

    private static void RequireLoadableFunction(WasmFunctionDefinition function)
    {
        var parameters = function.Type.Parameters.Length;
        if (parameters > MaximumFunctionParameters)
        {
            throw Limit($"the method compiles to a WebAssembly function with {parameters:N0} parameters, over the " +
                $"{MaximumFunctionParameters:N0} that browsers, Node and Wasmtime accept; pass the values in fewer " +
                "parameters, for example in an array or an object", function.Name);
        }
        var locals = parameters + DeclaredLocals(function.Body);
        if (locals > MaximumFunctionLocals)
        {
            throw Limit($"the method compiles to a WebAssembly function with {locals:N0} locals, over the " +
                $"{MaximumFunctionLocals:N0} that browsers, Node and Wasmtime accept; split it into smaller methods",
                function.Name);
        }
        if (function.Body.Length > MaximumFunctionBodySize)
        {
            throw Limit($"the method compiles to a {function.Body.Length:N0}-byte WebAssembly function, over the " +
                $"{MaximumFunctionBodySize:N0}-byte limit that browsers and Node enforce; split it into smaller methods",
                function.Name);
        }

        static CompilerException Limit(string message, string method) =>
            new(new CompilerDiagnostic(DiagnosticCode.FunctionLimit, message, method));
    }

    // A body starts with its locals: a count of runs, each a count and a value type. A reference
    // type that names its heap type (0x63, 0x64) carries the heap type after it.
    private static long DeclaredLocals(byte[] body)
    {
        var position = 0;
        var runs = ReadUnsigned(body, ref position);
        long locals = 0;
        for (ulong run = 0; run < runs; run++)
        {
            locals += (long)ReadUnsigned(body, ref position);
            if (body[position++] is 0x63 or 0x64) ReadUnsigned(body, ref position);
        }
        return locals;
    }

    private static ulong ReadUnsigned(byte[] bytes, ref int position)
    {
        ulong value = 0;
        for (var shift = 0; ; shift += 7)
        {
            var next = bytes[position++];
            value |= (ulong)(next & 0x7f) << shift;
            if ((next & 0x80) == 0) return value;
        }
    }

    private void WriteDataSection(
        IWasmBinaryWriter module,
        IReadOnlyList<DataSegment> segments,
        WasmTargetLayout target)
    {
        var payloadBuffer = new WasmBinaryBuffer();
        var payload = _writerFactory(payloadBuffer);
        var coalesced = Coalesce(segments);
        _unsigned.Encode(payload, (uint)coalesced.Count);
        foreach (var segment in coalesced)
        {
            payload.Write([0x00]);
            var instructions = _instructionWriterFactory(payload);
            if (target.UsesMemory64)
            {
                instructions.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I64Constant,
                    WasmInstructionOperand.Signed64(segment.Address)));
            }
            else
            {
                instructions.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I32Constant,
                    WasmInstructionOperand.Signed(segment.Address)));
            }
            instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            _unsigned.Encode(payload, (uint)segment.Data.Length);
            payload.Write(segment.Data.AsSpan());
        }
        _sections.Write(module, 11, _snapshotReaderFactory(payloadBuffer).Read());
    }

    // A segment that starts where the one before it ends joins it: the same bytes at the same addresses, written
    // in the same order, so the memory image does not change. One segment per string, descriptor and stack-trace
    // name would otherwise pass the 100,000 data segments that engines and validators accept.
    private static List<DataSegment> Coalesce(IReadOnlyList<DataSegment> segments)
    {
        var coalesced = new List<DataSegment>();
        var first = 0;
        while (first < segments.Count)
        {
            var start = segments[first];
            var end = (long)start.Address + start.Data.Length;
            var next = first + 1;
            while (next < segments.Count && segments[next].Address == end)
            {
                end += segments[next].Data.Length;
                next++;
            }

            if (next == first + 1)
            {
                coalesced.Add(start);
            }
            else
            {
                var data = ImmutableArray.CreateBuilder<byte>(checked((int)(end - start.Address)));
                for (var index = first; index < next; index++)
                {
                    data.AddRange(segments[index].Data);
                }
                coalesced.Add(new(start.Address, data.MoveToImmutable()));
            }
            first = next;
        }
        return coalesced;
    }

    private void WriteNameSection(
        IWasmBinaryWriter module,
        IReadOnlyList<WasmFunctionImport> imports,
        IReadOnlyList<WasmFunctionDefinition> functions)
    {
        var namesBuffer = new WasmBinaryBuffer();
        var names = _writerFactory(namesBuffer);
        _unsigned.Encode(names, (uint)(imports.Count + functions.Count));
        var index = 0;
        foreach (var import in imports)
        {
            _unsigned.Encode(names, (uint)index++);
            _utf8.Encode(names, $"{import.Module}.{import.Name}");
        }
        foreach (var function in functions)
        {
            _unsigned.Encode(names, (uint)index++);
            _utf8.Encode(names, function.Name);
        }

        var subsectionBuffer = new WasmBinaryBuffer();
        var subsection = _writerFactory(subsectionBuffer);
        subsection.Write([1]);
        _unsigned.Encode(subsection, (uint)namesBuffer.Length);
        subsection.Write(_snapshotReaderFactory(namesBuffer).Read());

        var payloadBuffer = new WasmBinaryBuffer();
        var payload = _writerFactory(payloadBuffer);
        _utf8.Encode(payload, "name");
        payload.Write(_snapshotReaderFactory(subsectionBuffer).Read());
        _sections.Write(module, 0, _snapshotReaderFactory(payloadBuffer).Read());
    }
}
