using System;

namespace NetWasm.Compiler.Core;

/// <summary>
/// The linear-memory addressing width selected for an application module.
/// This is deliberately separate from CLI value kinds: a managed reference is
/// a logical reference in both targets, but has a different Wasm transport
/// representation on Memory64.
/// </summary>
public enum WasmTarget
{
    Wasm32,
    Wasm64,
}

/// <summary>
/// Frozen physical-layout facts for one WebAssembly memory model.
/// Fixed semantic identifiers are never widened merely because addresses are.
/// </summary>
public sealed record WasmTargetLayout
{
    private WasmTargetLayout(
        WasmTarget target,
        int AddressSize,
        int ObjectReferenceSize,
        int ObjectReferenceAlignment,
        int ObjectHeaderSize)
    {
        Target = target;
        this.AddressSize = AddressSize;
        this.ObjectReferenceSize = ObjectReferenceSize;
        this.ObjectReferenceAlignment = ObjectReferenceAlignment;
        this.ObjectHeaderSize = ObjectHeaderSize;
    }

    public static WasmTargetLayout Wasm32 { get; } = new(
        WasmTarget.Wasm32,
        AddressSize: sizeof(int),
        ObjectReferenceSize: sizeof(int),
        ObjectReferenceAlignment: sizeof(int),
        ObjectHeaderSize: sizeof(int));

    public static WasmTargetLayout Wasm64 { get; } = new(
        WasmTarget.Wasm64,
        AddressSize: sizeof(long),
        ObjectReferenceSize: sizeof(long),
        ObjectReferenceAlignment: sizeof(long),
        ObjectHeaderSize: sizeof(long));

    public WasmTarget Target { get; }
    public int AddressSize { get; }
    public int ObjectReferenceSize { get; }
    public int ObjectReferenceAlignment { get; }
    public int ObjectHeaderSize { get; }
    public static int TypeIdSize => sizeof(int);
    public static int SemanticLengthSize => sizeof(int);
    public bool UsesMemory64 => Target == WasmTarget.Wasm64;

    public static WasmTargetLayout For(WasmTarget target) => target switch
    {
        WasmTarget.Wasm32 => Wasm32,
        WasmTarget.Wasm64 => Wasm64,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };

    public static int Align(int value, int alignment)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(alignment));
        }
        checked
        {
            return (value + alignment - 1) & -alignment;
        }
    }

    public int GetStorageSize(CliTypeIdentity type) => type.StackKind switch
    {
        CliValueKind.ManagedReference or CliValueKind.ManagedAddress => AddressSize,
        CliValueKind.I4 => sizeof(int),
        CliValueKind.I8 or CliValueKind.F8 => sizeof(long),
        CliValueKind.F4 => sizeof(float),
        CliValueKind.NativeInt => AddressSize,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type.StackKind,
            "The type has no scalar target storage size."),
    };

    public int GetStorageAlignment(CliTypeIdentity type) => type.StackKind switch
    {
        CliValueKind.ManagedReference or CliValueKind.ManagedAddress => ObjectReferenceAlignment,
        CliValueKind.I4 => sizeof(int),
        CliValueKind.I8 or CliValueKind.F8 => sizeof(long),
        CliValueKind.F4 => sizeof(float),
        CliValueKind.NativeInt => AddressSize,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type.StackKind,
            "The type has no scalar target storage alignment."),
    };
}
