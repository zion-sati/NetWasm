// Adapted from dotnet/runtime System.Runtime.InteropServices metadata
// declarations. The upstream implementation is licensed under MIT.
// These declarations describe metadata only; no COM, native marshalling,
// GCHandle, or P/Invoke execution is provided here.

namespace System.Runtime.InteropServices
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class AllowReversePInvokeCallsAttribute : Attribute
    {
        public AllowReversePInvokeCallsAttribute() { }
    }

    public enum Architecture
    {
        X86,
        X64,
        Arm,
        Arm64,
        Wasm,
        S390x,
        LoongArch64,
        Armv6,
        Ppc64le,
        RiscV64
    }

    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Interface | AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    public sealed class BestFitMappingAttribute : Attribute
    {
        public BestFitMappingAttribute(bool bestFitMapping) => BestFitMapping = bestFitMapping;
        public bool BestFitMapping { get; }
        public bool ThrowOnUnmappableChar;
    }

    public enum CharSet
    {
        None = 1,
        Ansi = 2,
        Unicode = 3,
        Auto = 4
    }

    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, Inherited = false)]
    public sealed class ClassInterfaceAttribute : Attribute
    {
        public ClassInterfaceAttribute(ClassInterfaceType classInterfaceType) => Value = classInterfaceType;
        public ClassInterfaceAttribute(short classInterfaceType) => Value = (ClassInterfaceType)classInterfaceType;
        public ClassInterfaceType Value { get; }
    }

    public enum ClassInterfaceType
    {
        None = 0,
        AutoDispatch = 1,
        AutoDual = 2
    }

    [AttributeUsage(AttributeTargets.Interface, Inherited = false)]
    public sealed class CoClassAttribute : Attribute
    {
        public CoClassAttribute(Type coClass) => CoClass = coClass;
        public Type CoClass { get; }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ComDefaultInterfaceAttribute : Attribute
    {
        public ComDefaultInterfaceAttribute(Type defaultInterface) => Value = defaultInterface;
        public Type Value { get; }
    }

    [AttributeUsage(AttributeTargets.Interface, Inherited = false)]
    public sealed class ComEventInterfaceAttribute : Attribute
    {
        public ComEventInterfaceAttribute(Type sourceInterface, Type eventProvider)
        {
            SourceInterface = sourceInterface;
            EventProvider = eventProvider;
        }

        public Type SourceInterface { get; }
        public Type EventProvider { get; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = false)]
    public sealed class ComImportAttribute : Attribute
    {
        public ComImportAttribute() { }
    }

    public enum ComInterfaceType
    {
        InterfaceIsDual = 0,
        InterfaceIsIUnknown = 1,
        InterfaceIsIDispatch = 2,
        InterfaceIsIInspectable = 3
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class ComSourceInterfacesAttribute : Attribute
    {
        public ComSourceInterfacesAttribute(string sourceInterfaces) => Value = sourceInterfaces;
        public ComSourceInterfacesAttribute(Type sourceInterface) => Value = sourceInterface?.ToString() ?? string.Empty;
        public ComSourceInterfacesAttribute(Type sourceInterface1, Type sourceInterface2) => Value = Join(sourceInterface1, sourceInterface2);
        public ComSourceInterfacesAttribute(Type sourceInterface1, Type sourceInterface2, Type sourceInterface3) => Value = Join(sourceInterface1, sourceInterface2, sourceInterface3);
        public ComSourceInterfacesAttribute(Type sourceInterface1, Type sourceInterface2, Type sourceInterface3, Type sourceInterface4) => Value = Join(sourceInterface1, sourceInterface2, sourceInterface3, sourceInterface4);
        public string Value { get; }

        private static string Join(params Type[] sourceInterfaces)
        {
            var result = string.Empty;
            for (var index = 0; index < sourceInterfaces.Length; index++)
            {
                if (index != 0) result += "\0";
                result += sourceInterfaces[index]?.ToString() ?? string.Empty;
            }
            return result;
        }
    }

    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Interface | AttributeTargets.Class |
        AttributeTargets.Struct | AttributeTargets.Delegate | AttributeTargets.Enum | AttributeTargets.Field |
        AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
    public sealed class ComVisibleAttribute : Attribute
    {
        public ComVisibleAttribute(bool visibility) => Value = visibility;
        public bool Value { get; }
    }

    [Flags]
    public enum CreateObjectFlags
    {
        None = 0,
        TrackerObject = 1,
        UniqueInstance = 2,
        Aggregation = 4,
        Unwrap = 8
    }

    [AttributeUsage(AttributeTargets.Module, Inherited = false)]
    public sealed class DefaultCharSetAttribute : Attribute
    {
        public DefaultCharSetAttribute(CharSet charSet) => CharSet = charSet;
        public CharSet CharSet { get; }
    }

    [Flags]
    public enum DllImportSearchPath
    {
        LegacyBehavior = 0x0,
        AssemblyDirectory = 0x2,
        UseDllDirectoryForDependencies = 0x100,
        ApplicationDirectory = 0x200,
        UserDirectories = 0x400,
        System32 = 0x800,
        SafeDirectories = 0x1000
    }

    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class DefaultDllImportSearchPathsAttribute : Attribute
    {
        public DefaultDllImportSearchPathsAttribute(DllImportSearchPath paths) => Paths = paths;
        public DllImportSearchPath Paths { get; }
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class DefaultParameterValueAttribute : Attribute
    {
        public DefaultParameterValueAttribute(object? value) => Value = value;
        public object? Value { get; }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Event, Inherited = false)]
    public sealed class DispIdAttribute : Attribute
    {
        public DispIdAttribute(int dispId) => Value = dispId;
        public int Value { get; }
    }

    [AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
    public sealed class DynamicInterfaceCastableImplementationAttribute : Attribute
    {
        public DynamicInterfaceCastableImplementationAttribute() { }
    }

    public enum ExtendedLayoutKind
    {
        CStruct = 0,
        CUnion = 1
    }

    [AttributeUsage(AttributeTargets.Struct, Inherited = false)]
    public sealed class ExtendedLayoutAttribute : Attribute
    {
        public ExtendedLayoutAttribute(ExtendedLayoutKind layoutKind) { }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class FieldOffsetAttribute : Attribute
    {
        public FieldOffsetAttribute(int offset) => Value = offset;
        public int Value { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Interface | AttributeTargets.Class |
        AttributeTargets.Enum | AttributeTargets.Struct | AttributeTargets.Delegate, Inherited = false)]
    public sealed class GuidAttribute : Attribute
    {
        public GuidAttribute(string guid) => Value = guid;
        public string Value { get; }
    }

    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class InAttribute : Attribute
    {
        public InAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Interface, Inherited = false)]
    public sealed class InterfaceTypeAttribute : Attribute
    {
        public InterfaceTypeAttribute(ComInterfaceType interfaceType) => Value = interfaceType;
        public InterfaceTypeAttribute(short interfaceType) => Value = (ComInterfaceType)interfaceType;
        public ComInterfaceType Value { get; }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class LCIDConversionAttribute : Attribute
    {
        public LCIDConversionAttribute(int lcid) => Value = lcid;
        public int Value { get; }
    }

    public enum LayoutKind
    {
        Sequential = 0,
        Extended = 1,
        Explicit = 2,
        Auto = 3
    }

    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.ReturnValue, Inherited = false)]
    public sealed class MarshalAsAttribute : Attribute
    {
        public MarshalAsAttribute(UnmanagedType unmanagedType) => Value = unmanagedType;
        public MarshalAsAttribute(short unmanagedType) => Value = (UnmanagedType)unmanagedType;
        public UnmanagedType Value { get; }
        public VarEnum SafeArraySubType;
        public Type? SafeArrayUserDefinedSubType;
        public int IidParameterIndex;
        public UnmanagedType ArraySubType;
        public short SizeParamIndex;
        public int SizeConst;
        public string? MarshalType;
        public Type? MarshalTypeRef;
        public string? MarshalCookie;
    }

    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class OptionalAttribute : Attribute
    {
        public OptionalAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class OutAttribute : Attribute
    {
        public OutAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class PreserveSigAttribute : Attribute
    {
        public PreserveSigAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ProgIdAttribute : Attribute
    {
        public ProgIdAttribute(string progId) => Value = progId;
        public string Value { get; }
    }

    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
    public sealed class StructLayoutAttribute : Attribute
    {
        public StructLayoutAttribute(LayoutKind layoutKind) => Value = layoutKind;
        public StructLayoutAttribute(short layoutKind) => Value = (LayoutKind)layoutKind;
        public LayoutKind Value { get; }
        public int Pack;
        public int Size;
        public CharSet CharSet;
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class SuppressGCTransitionAttribute : Attribute
    {
        public SuppressGCTransitionAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Enum | AttributeTargets.Struct |
        AttributeTargets.Delegate, AllowMultiple = false, Inherited = false)]
    public sealed class TypeIdentifierAttribute : Attribute
    {
        public TypeIdentifierAttribute() { }
        public TypeIdentifierAttribute(string? scope, string? identifier)
        {
            Scope = scope;
            Identifier = identifier;
        }

        public string? Scope { get; }
        public string? Identifier { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class TypeMapAssemblyTargetAttribute<TTypeMapGroup> : Attribute
    {
        public TypeMapAssemblyTargetAttribute(string assemblyName) { }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class TypeMapAssociationAttribute<TTypeMapGroup> : Attribute
    {
        public TypeMapAssociationAttribute(Type source, Type proxy) { }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class TypeMapAttribute<TTypeMapGroup> : Attribute
    {
        public TypeMapAttribute(string value, Type target) { }
        public TypeMapAttribute(string value, Type target, Type trimTarget) { }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class UnmanagedCallConvAttribute : Attribute
    {
        public UnmanagedCallConvAttribute() { }
        public Type[]? CallConvs;
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class UnmanagedCallersOnlyAttribute : Attribute
    {
        public UnmanagedCallersOnlyAttribute() { }
        public Type[]? CallConvs;
        public string? EntryPoint;
        public Type? AssociatedSourceType;
    }

    public enum UnmanagedType
    {
        Bool = 0x2,
        I1 = 0x3,
        U1 = 0x4,
        I2 = 0x5,
        U2 = 0x6,
        I4 = 0x7,
        U4 = 0x8,
        I8 = 0x9,
        U8 = 0xa,
        R4 = 0xb,
        R8 = 0xc,
        Currency = 0xf,
        BStr = 0x13,
        LPStr = 0x14,
        LPWStr = 0x15,
        LPTStr = 0x16,
        ByValTStr = 0x17,
        IUnknown = 0x19,
        IDispatch = 0x1a,
        Struct = 0x1b,
        Interface = 0x1c,
        SafeArray = 0x1d,
        ByValArray = 0x1e,
        SysInt = 0x1f,
        SysUInt = 0x20,
        VBByRefStr = 0x22,
        AnsiBStr = 0x23,
        TBStr = 0x24,
        VariantBool = 0x25,
        FunctionPtr = 0x26,
        AsAny = 0x28,
        LPArray = 0x2a,
        LPStruct = 0x2b,
        CustomMarshaler = 0x2c,
        Error = 0x2d,
        IInspectable = 0x2e,
        HString = 0x2f,
        LPUTF8Str = 0x30
    }

    public enum VarEnum
    {
        VT_EMPTY = 0,
        VT_NULL = 1,
        VT_I2 = 2,
        VT_I4 = 3,
        VT_R4 = 4,
        VT_R8 = 5,
        VT_CY = 6,
        VT_DATE = 7,
        VT_BSTR = 8,
        VT_DISPATCH = 9,
        VT_ERROR = 10,
        VT_BOOL = 11,
        VT_VARIANT = 12,
        VT_UNKNOWN = 13,
        VT_DECIMAL = 14,
        VT_I1 = 16,
        VT_UI1 = 17,
        VT_UI2 = 18,
        VT_UI4 = 19,
        VT_I8 = 20,
        VT_UI8 = 21,
        VT_INT = 22,
        VT_UINT = 23,
        VT_VOID = 24,
        VT_HRESULT = 25,
        VT_PTR = 26,
        VT_SAFEARRAY = 27,
        VT_CARRAY = 28,
        VT_USERDEFINED = 29,
        VT_LPSTR = 30,
        VT_LPWSTR = 31,
        VT_RECORD = 36,
        VT_FILETIME = 64,
        VT_BLOB = 65,
        VT_STREAM = 66,
        VT_STORAGE = 67,
        VT_STREAMED_OBJECT = 68,
        VT_STORED_OBJECT = 69,
        VT_BLOB_OBJECT = 70,
        VT_CF = 71,
        VT_CLSID = 72,
        VT_VECTOR = 0x1000,
        VT_ARRAY = 0x2000,
        VT_BYREF = 0x4000
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class WasmImportLinkageAttribute : Attribute
    {
        public WasmImportLinkageAttribute() { }
    }
}
