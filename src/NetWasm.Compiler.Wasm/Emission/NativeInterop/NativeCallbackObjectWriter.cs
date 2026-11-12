using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.ModuleEncoding;

namespace NetWasm.Compiler.Wasm.Emission.NativeInterop;

/// <summary>
/// Adapts the callback plan to the narrow LLVM Wasm object contract consumed by
/// the native linker. LLD owns final table placement and resolves each getter's
/// relocated table address. Each unmanaged calli trampoline calls its last argument
/// through the linked function table with the call's C ABI signature (issue 226).
/// </summary>
internal sealed class NativeCallbackObjectWriter : INativeCallbackObjectWriter
{
    private static readonly byte[] Header =
        [0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00];

    private const byte CustomSection = 0;
    private const byte TypeSection = 1;
    private const byte ImportSection = 2;
    private const byte FunctionSection = 3;
    private const byte ElementSection = 9;
    private const byte CodeSection = 10;
    private const byte FunctionType = 0x60;
    private const byte FunctionImport = 0;
    private const byte TableImport = 1;
    private const byte MemoryImport = 2;
    private const byte FunctionSymbol = 0;
    private const byte TableSymbol = 5;
    private const byte UndefinedExplicitName = 0x50;
    private const byte Undefined = 0x10;
    private const byte SymbolTableSubsection = 8;
    private const byte I32Const = 0x41;
    private const byte I64Const = 0x42;
    private const byte LocalGet = 0x20;
    private const byte Call = 0x10;
    private const byte CallIndirect = 0x11;
    private const byte End = 0x0b;
    private const byte FunctionIndexLebRelocation = 0;
    private const byte TableIndexSlebRelocation = 1;
    private const byte TableIndexSleb64Relocation = 18;
    private const byte TypeIndexLebRelocation = 6;
    private const byte TableNumberLebRelocation = 20;

    private readonly IUnsignedLeb128Encoder _unsigned;
    private readonly IUtf8StringEncoder _utf8;
    private readonly IWasmSectionWriter _sections;
    private readonly Func<WasmBinaryBuffer, IWasmBinaryWriter> _writers;

    public NativeCallbackObjectWriter(
        IUnsignedLeb128Encoder unsigned,
        IUtf8StringEncoder utf8,
        IWasmSectionWriter sections,
        Func<WasmBinaryBuffer, IWasmBinaryWriter> writers)
    {
        ArgumentNullException.ThrowIfNull(unsigned);
        ArgumentNullException.ThrowIfNull(utf8);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(writers);
        _unsigned = unsigned;
        _utf8 = utf8;
        _sections = sections;
        _writers = writers;
    }

    public byte[] Write(
        NativeCallbackPlan callbacks,
        ImmutableArray<NativeCallTrampoline> trampolines,
        WasmTarget target)
    {
        ArgumentNullException.ThrowIfNull(callbacks);
        trampolines = trampolines.IsDefault ? [] : trampolines;
        var targetLayout = WasmTargetLayout.For(target);
        if (callbacks.Methods.Length == 0 && trampolines.IsEmpty)
        {
            return [];
        }

        var moduleBuffer = new WasmBinaryBuffer();
        var module = _writers(moduleBuffer);
        module.Write(Header);
        WriteTypeSection(module, callbacks, trampolines, targetLayout);
        WriteImportSection(module, callbacks, trampolines, targetLayout);
        var named = callbacks.Methods.Where(callback => callback.IsNamed).ToArray();
        if (named.Length != 0 || !callbacks.AddressedMethods.IsEmpty || !trampolines.IsEmpty)
        {
            WriteFunctionSection(
                module,
                callbacks,
                named,
                callbacks.AddressedMethods.Length,
                trampolines);
        }
        if (!callbacks.AddressedMethods.IsEmpty)
        {
            WriteElementSection(module, callbacks, targetLayout);
        }
        var relocations = WriteCodeSection(module, callbacks, named, trampolines, targetLayout);
        WriteLinkingSection(module, callbacks, trampolines);
        if (relocations.Count != 0)
        {
            WriteRelocationSection(
                module,
                relocations,
                callbacks.AddressedMethods.IsEmpty ? 3 : 4);
        }
        if (targetLayout.UsesMemory64)
        {
            WriteTargetFeaturesSection(module);
        }
        return moduleBuffer.Snapshot();
    }

    private void WriteTypeSection(
        IWasmBinaryWriter module,
        NativeCallbackPlan callbacks,
        ImmutableArray<NativeCallTrampoline> trampolines,
        WasmTargetLayout target)
    {
        WriteSection(module, TypeSection, payload =>
        {
            WriteCount(payload, TrampolineTypeBase(callbacks) + trampolines.Length * 2);
            foreach (var callback in callbacks.Methods)
            {
                var signature = callback.Abi.PhysicalSignature;
                payload.Write([FunctionType]);
                WriteCount(payload, signature.ParameterTypes.Length);
                foreach (var parameter in signature.ParameterTypes)
                {
                    payload.Write([(byte)WasmValueTypes.FromCli(parameter, target)]);
                }
                WriteResult(payload, signature.ReturnType, target);
            }

            if (!callbacks.AddressedMethods.IsEmpty)
            {
                payload.Write([FunctionType, 0x00, 0x01]);
                payload.Write([(byte)(target.UsesMemory64
                    ? WasmValueType.I64
                    : WasmValueType.I32)]);
            }

            // Each trampoline's own type, then the type of the function it calls: the same without
            // the trailing function pointer.
            foreach (var trampoline in trampolines)
            {
                WriteFunctionType(payload, trampoline.Type.Parameters, trampoline.Type.Result, target);
                WriteFunctionType(payload, trampoline.Type.Parameters[..^1], trampoline.Type.Result, target);
            }
        });
    }

    private void WriteFunctionType(
        IWasmBinaryWriter payload,
        ImmutableArray<CliValueKind> parameters,
        CliValueKind result,
        WasmTargetLayout target)
    {
        payload.Write([FunctionType]);
        WriteCount(payload, parameters.Length);
        foreach (var parameter in parameters)
        {
            payload.Write([(byte)WasmValueTypes.FromCli(parameter, target)]);
        }
        WriteResult(payload, result, target);
    }

    // The getter's type follows the callbacks' types; the trampolines' types follow both.
    private static int TrampolineTypeBase(NativeCallbackPlan callbacks) =>
        callbacks.Methods.Length + (callbacks.AddressedMethods.IsEmpty ? 0 : 1);

    private static bool ImportsTable(
        NativeCallbackPlan callbacks,
        ImmutableArray<NativeCallTrampoline> trampolines) =>
        !callbacks.AddressedMethods.IsEmpty || !trampolines.IsEmpty;

    private void WriteImportSection(
        IWasmBinaryWriter module,
        NativeCallbackPlan callbacks,
        ImmutableArray<NativeCallTrampoline> trampolines,
        WasmTargetLayout target)
    {
        WriteSection(module, ImportSection, payload =>
        {
            WriteCount(payload, callbacks.Methods.Length +
                (ImportsTable(callbacks, trampolines) ? 2 : 1));
            for (var index = 0; index < callbacks.Methods.Length; index++)
            {
                _utf8.Encode(payload, RuntimeAbi.ApplicationModule);
                _utf8.Encode(payload, callbacks.Methods[index].ThunkExportName);
                payload.Write([FunctionImport]);
                WriteCount(payload, index);
            }

            _utf8.Encode(payload, "env");
            _utf8.Encode(payload, "__linear_memory");
            payload.Write([MemoryImport, target.UsesMemory64 ? (byte)0x04 : (byte)0x00, 0x00]);

            if (ImportsTable(callbacks, trampolines))
            {
                _utf8.Encode(payload, "env");
                _utf8.Encode(payload, "__indirect_function_table");
                payload.Write([
                    TableImport,
                    0x70,
                    target.UsesMemory64 ? (byte)0x04 : (byte)0x00,
                    0x01,
                ]);
            }
        });
    }

    private void WriteFunctionSection(
        IWasmBinaryWriter module,
        NativeCallbackPlan callbacks,
        NativeCallbackMethodPlan[] named,
        int addressedCount,
        ImmutableArray<NativeCallTrampoline> trampolines)
    {
        WriteSection(module, FunctionSection, payload =>
        {
            WriteCount(payload, named.Length + addressedCount + trampolines.Length);
            foreach (var callback in named)
            {
                WriteCount(payload, callbacks.Methods.IndexOf(callback));
            }
            for (var index = 0; index < addressedCount; index++)
            {
                WriteCount(payload, callbacks.Methods.Length);
            }
            for (var index = 0; index < trampolines.Length; index++)
            {
                WriteCount(payload, TrampolineTypeBase(callbacks) + index * 2);
            }
        });
    }

    private void WriteElementSection(
        IWasmBinaryWriter module,
        NativeCallbackPlan callbacks,
        WasmTargetLayout target)
    {
        WriteSection(module, ElementSection, payload =>
        {
            WriteCount(payload, 1);
            payload.Write([
                0x00,
                target.UsesMemory64 ? I64Const : I32Const,
                0x00,
                End,
            ]);
            WriteCount(payload, callbacks.AddressedMethods.Length);
            foreach (var callback in callbacks.AddressedMethods)
            {
                WriteCount(payload, CallbackTargetIndex(callbacks, callback));
            }
        });
    }

    private List<ObjectRelocation> WriteCodeSection(
        IWasmBinaryWriter module,
        NativeCallbackPlan callbacks,
        NativeCallbackMethodPlan[] named,
        ImmutableArray<NativeCallTrampoline> trampolines,
        WasmTargetLayout target)
    {
        var relocations = new List<ObjectRelocation>(
            named.Length + callbacks.AddressedMethods.Length + trampolines.Length * 2);
        WriteSection(module, CodeSection, (payload, payloadLength) =>
        {
            WriteCount(payload, named.Length + callbacks.AddressedMethods.Length + trampolines.Length);
            foreach (var callback in named)
            {
                var bodyBuffer = new WasmBinaryBuffer();
                var body = _writers(bodyBuffer);
                WriteCount(body, 0);
                for (var parameter = 0;
                     parameter < callback.Abi.PhysicalSignature.ParameterTypes.Length;
                     parameter++)
                {
                    body.Write([LocalGet]);
                    WriteCount(body, parameter);
                }
                body.Write([Call]);
                var relocationOffset = bodyBuffer.Length;
                WritePaddedZero(body, 5);
                body.Write([End]);

                WriteCount(payload, bodyBuffer.Length);
                var bodyOffset = payloadLength();
                payload.Write(bodyBuffer.Snapshot());
                relocations.Add(new(
                    FunctionIndexLebRelocation,
                    bodyOffset + relocationOffset,
                    callbacks.Methods.IndexOf(callback)));
            }
            foreach (var callback in callbacks.AddressedMethods)
            {
                var addressWidth = target.UsesMemory64 ? 10 : 5;
                var bodyLength = 3 + addressWidth;
                WriteCount(payload, bodyLength);
                payload.Write([0x00, target.UsesMemory64 ? I64Const : I32Const]);
                relocations.Add(new(
                    target.UsesMemory64
                        ? TableIndexSleb64Relocation
                        : TableIndexSlebRelocation,
                    payloadLength(),
                    CallbackTargetIndex(callbacks, callback)));
                WritePaddedZero(payload, addressWidth);
                payload.Write([End]);
            }
            for (var index = 0; index < trampolines.Length; index++)
            {
                // local.get each argument, then the pointer; call_indirect (type) (table).
                var arguments = trampolines[index].Type.Parameters.Length;
                var bodyBuffer = new WasmBinaryBuffer();
                var body = _writers(bodyBuffer);
                WriteCount(body, 0);
                for (var parameter = 0; parameter < arguments; parameter++)
                {
                    body.Write([LocalGet]);
                    WriteCount(body, parameter);
                }
                body.Write([CallIndirect]);
                var typeOffset = bodyBuffer.Length;
                WritePaddedZero(body, 5);
                var tableOffset = bodyBuffer.Length;
                WritePaddedZero(body, 5);
                body.Write([End]);

                WriteCount(payload, bodyBuffer.Length);
                var bodyOffset = payloadLength();
                payload.Write(bodyBuffer.Snapshot());
                relocations.Add(new(
                    TypeIndexLebRelocation,
                    bodyOffset + typeOffset,
                    TrampolineTypeBase(callbacks) + index * 2 + 1));
                relocations.Add(new(
                    TableNumberLebRelocation,
                    bodyOffset + tableOffset,
                    TableSymbolIndex(callbacks, named.Length, trampolines)));
            }
        });
        return relocations;
    }

    // The symbol table lists the callback imports, the named callbacks, the getters, the
    // trampolines and then the table.
    private static int TableSymbolIndex(
        NativeCallbackPlan callbacks,
        int namedCount,
        ImmutableArray<NativeCallTrampoline> trampolines) =>
        callbacks.Methods.Length + namedCount + callbacks.AddressedMethods.Length + trampolines.Length;

    private void WriteLinkingSection(
        IWasmBinaryWriter module,
        NativeCallbackPlan callbacks,
        ImmutableArray<NativeCallTrampoline> trampolines)
    {
        WriteCustomSection(module, "linking", payload =>
        {
            WriteCount(payload, 2);
            WriteSubsection(payload, SymbolTableSubsection, symbols =>
            {
                var named = callbacks.Methods
                    .Where(callback => callback.IsNamed)
                    .ToArray();
                WriteCount(symbols, checked(TableSymbolIndex(callbacks, named.Length, trampolines) +
                    (ImportsTable(callbacks, trampolines) ? 1 : 0)));
                for (var index = 0; index < callbacks.Methods.Length; index++)
                {
                    symbols.Write([FunctionSymbol, UndefinedExplicitName]);
                    WriteCount(symbols, index);
                    _utf8.Encode(symbols, callbacks.Methods[index].RuntimeImportSymbol);
                }
                for (var index = 0; index < named.Length; index++)
                {
                    symbols.Write([FunctionSymbol, 0x00]);
                    WriteCount(symbols, callbacks.Methods.Length + index);
                    _utf8.Encode(symbols, named[index].NativeSymbol);
                }
                for (var index = 0; index < callbacks.AddressedMethods.Length; index++)
                {
                    symbols.Write([FunctionSymbol, 0x00]);
                    WriteCount(symbols, callbacks.Methods.Length + named.Length + index);
                    _utf8.Encode(symbols, callbacks.AddressedMethods[index].GetterName!);
                }
                for (var index = 0; index < trampolines.Length; index++)
                {
                    symbols.Write([FunctionSymbol, 0x00]);
                    WriteCount(symbols, callbacks.Methods.Length + named.Length +
                        callbacks.AddressedMethods.Length + index);
                    _utf8.Encode(symbols, trampolines[index].Symbol);
                }
                if (ImportsTable(callbacks, trampolines))
                {
                    symbols.Write([TableSymbol, Undefined, 0x00]);
                }
            });
        });
    }

    private void WriteRelocationSection(
        IWasmBinaryWriter module,
        IReadOnlyList<ObjectRelocation> relocations,
        int codeSectionIndex)
    {
        WriteCustomSection(module, "reloc.CODE", payload =>
        {
            WriteCount(payload, codeSectionIndex);
            WriteCount(payload, relocations.Count);
            foreach (var relocation in relocations)
            {
                payload.Write([relocation.Kind]);
                WriteCount(payload, relocation.Offset);
                WriteCount(payload, relocation.SymbolIndex);
            }
        });
    }

    private static int CallbackTargetIndex(
        NativeCallbackPlan callbacks,
        NativeCallbackMethodPlan callback)
    {
        if (!callback.IsNamed)
        {
            return callbacks.Methods.IndexOf(callback);
        }
        return callbacks.Methods.Length + callbacks.Methods
            .Where(method => method.IsNamed)
            .ToList()
            .IndexOf(callback);
    }

    private void WriteTargetFeaturesSection(IWasmBinaryWriter module)
    {
        WriteCustomSection(module, "target_features", payload =>
        {
            WriteCount(payload, 1);
            payload.Write([0x2b]);
            _utf8.Encode(payload, "memory64");
        });
    }

    private void WriteResult(
        IWasmBinaryWriter payload,
        CliValueKind result,
        WasmTargetLayout target)
    {
        if (result == CliValueKind.Void)
        {
            WriteCount(payload, 0);
            return;
        }
        WriteCount(payload, 1);
        payload.Write([(byte)WasmValueTypes.FromCli(result, target)]);
    }

    private void WriteCustomSection(
        IWasmBinaryWriter module,
        string name,
        Action<IWasmBinaryWriter> writePayload)
    {
        WriteSection(module, CustomSection, payload =>
        {
            _utf8.Encode(payload, name);
            writePayload(payload);
        });
    }

    private void WriteSubsection(
        IWasmBinaryWriter output,
        byte id,
        Action<IWasmBinaryWriter> writePayload) =>
        WriteSection(output, id, writePayload);

    private void WriteSection(
        IWasmBinaryWriter module,
        byte id,
        Action<IWasmBinaryWriter> writePayload) =>
        WriteSection(module, id, (payload, _) => writePayload(payload));

    private void WriteSection(
        IWasmBinaryWriter module,
        byte id,
        Action<IWasmBinaryWriter, Func<int>> writePayload)
    {
        var payloadBuffer = new WasmBinaryBuffer();
        var payload = _writers(payloadBuffer);
        writePayload(payload, () => payloadBuffer.Length);
        _sections.Write(module, id, payloadBuffer.Snapshot());
    }

    private void WriteCount(IWasmBinaryWriter writer, int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        _unsigned.Encode(writer, checked((uint)value));
    }

    private static void WritePaddedZero(IWasmBinaryWriter writer, int width)
    {
        for (var index = 1; index < width; index++)
        {
            writer.Write([0x80]);
        }
        writer.Write([0x00]);
    }

    private readonly record struct ObjectRelocation(
        byte Kind,
        int Offset,
        int SymbolIndex);

}
