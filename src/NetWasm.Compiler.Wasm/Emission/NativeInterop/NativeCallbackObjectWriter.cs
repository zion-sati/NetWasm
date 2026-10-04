using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.ModuleEncoding;

namespace NetWasm.Compiler.Wasm.Emission.NativeInterop;

/// <summary>
/// Adapts the callback plan to the narrow LLVM Wasm object contract consumed by
/// the native linker. LLD owns final table placement and resolves each getter's
/// relocated table address.
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
    private const byte End = 0x0b;
    private const byte FunctionIndexLebRelocation = 0;
    private const byte TableIndexSlebRelocation = 1;
    private const byte TableIndexSleb64Relocation = 18;

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

    public byte[] Write(NativeCallbackPlan callbacks, WasmTarget target)
    {
        ArgumentNullException.ThrowIfNull(callbacks);
        var targetLayout = WasmTargetLayout.For(target);
        if (callbacks.Methods.Length == 0)
        {
            return [];
        }

        var moduleBuffer = new WasmBinaryBuffer();
        var module = _writers(moduleBuffer);
        module.Write(Header);
        WriteTypeSection(module, callbacks, targetLayout);
        WriteImportSection(module, callbacks, targetLayout);
        var named = callbacks.Methods.Where(callback => callback.IsNamed).ToArray();
        if (named.Length != 0 || !callbacks.AddressedMethods.IsEmpty)
        {
            WriteFunctionSection(
                module,
                callbacks,
                named,
                callbacks.AddressedMethods.Length);
        }
        if (!callbacks.AddressedMethods.IsEmpty)
        {
            WriteElementSection(module, callbacks, targetLayout);
        }
        var relocations = WriteCodeSection(module, callbacks, named, targetLayout);
        WriteLinkingSection(module, callbacks);
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
        WasmTargetLayout target)
    {
        WriteSection(module, TypeSection, payload =>
        {
            WriteCount(payload, callbacks.Methods.Length +
                (callbacks.AddressedMethods.IsEmpty ? 0 : 1));
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
        });
    }

    private void WriteImportSection(
        IWasmBinaryWriter module,
        NativeCallbackPlan callbacks,
        WasmTargetLayout target)
    {
        WriteSection(module, ImportSection, payload =>
        {
            WriteCount(payload, callbacks.Methods.Length +
                (!callbacks.AddressedMethods.IsEmpty ? 2 : 1));
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

            if (!callbacks.AddressedMethods.IsEmpty)
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
        int addressedCount)
    {
        WriteSection(module, FunctionSection, payload =>
        {
            WriteCount(payload, named.Length + addressedCount);
            foreach (var callback in named)
            {
                WriteCount(payload, callbacks.Methods.IndexOf(callback));
            }
            for (var index = 0; index < addressedCount; index++)
            {
                WriteCount(payload, callbacks.Methods.Length);
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
        WasmTargetLayout target)
    {
        var relocations = new List<ObjectRelocation>(
            named.Length + callbacks.AddressedMethods.Length);
        WriteSection(module, CodeSection, (payload, payloadLength) =>
        {
            WriteCount(payload, named.Length + callbacks.AddressedMethods.Length);
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
        });
        return relocations;
    }

    private void WriteLinkingSection(IWasmBinaryWriter module, NativeCallbackPlan callbacks)
    {
        WriteCustomSection(module, "linking", payload =>
        {
            WriteCount(payload, 2);
            WriteSubsection(payload, SymbolTableSubsection, symbols =>
            {
                var named = callbacks.Methods
                    .Where(callback => callback.IsNamed)
                    .ToArray();
                WriteCount(symbols, checked(callbacks.Methods.Length +
                    named.Length +
                    callbacks.AddressedMethods.Length +
                    (callbacks.AddressedMethods.IsEmpty ? 0 : 1)));
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
                if (!callbacks.AddressedMethods.IsEmpty)
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
