using System;
using System.Collections.Generic;
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
        Func<IWasmBinaryWriter, IWasmInstructionWriter> instructionWriterFactory)
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
        _unsigned = unsignedEncoder;
        _unsigned64 = unsigned64Encoder;
        _signed = signedEncoder;
        _signed64 = signed64Encoder;
        _utf8 = utf8;
        _sections = sections;
        _writerFactory = writerFactory;
        _snapshotReaderFactory = snapshotReaderFactory;
        _instructionWriterFactory = instructionWriterFactory;
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
            _unsigned.Encode(payload, (uint)function.Body.Length);
            payload.Write(function.Body);
        }
        _sections.Write(module, 10, _snapshotReaderFactory(payloadBuffer).Read());
    }

    private void WriteDataSection(
        IWasmBinaryWriter module,
        IReadOnlyList<DataSegment> segments,
        WasmTargetLayout target)
    {
        var payloadBuffer = new WasmBinaryBuffer();
        var payload = _writerFactory(payloadBuffer);
        _unsigned.Encode(payload, (uint)segments.Count);
        foreach (var segment in segments)
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
