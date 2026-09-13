// Adapted from dotnet/runtime System.Reflection metadata declarations.
// The upstream implementation is licensed under MIT.

namespace System.Reflection
{
    [AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
    public sealed class AssemblyCompanyAttribute : Attribute
    {
        public AssemblyCompanyAttribute(string company) => Company = company;
        public string Company { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
    public sealed class AssemblyConfigurationAttribute : Attribute
    {
        public AssemblyConfigurationAttribute(string configuration) => Configuration = configuration;
        public string Configuration { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
    public sealed class AssemblyCopyrightAttribute : Attribute
    {
        public AssemblyCopyrightAttribute(string copyright) => Copyright = copyright;
        public string Copyright { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
    public sealed class AssemblyDefaultAliasAttribute : Attribute
    {
        public AssemblyDefaultAliasAttribute(string defaultAlias) => DefaultAlias = defaultAlias;
        public string DefaultAlias { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
    public sealed class AssemblyDescriptionAttribute : Attribute
    {
        public AssemblyDescriptionAttribute(string description) => Description = description;
        public string Description { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
    public sealed class AssemblyFileVersionAttribute : Attribute
    {
        public AssemblyFileVersionAttribute(string version) => Version = version;
        public string Version { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
    public sealed class AssemblyInformationalVersionAttribute : Attribute
    {
        public AssemblyInformationalVersionAttribute(string informationalVersion) => InformationalVersion = informationalVersion;
        public string InformationalVersion { get; }
    }

    [Flags]
    public enum AssemblyNameFlags
    {
        None = 0x0000,
        PublicKey = 0x0001,
        Retargetable = 0x0100,
        EnableJITcompileOptimizer = 0x4000,
        EnableJITcompileTracking = 0x8000
    }

    [AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
    public sealed class AssemblyProductAttribute : Attribute
    {
        public AssemblyProductAttribute(string product) => Product = product;
        public string Product { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
    public sealed class AssemblyTitleAttribute : Attribute
    {
        public AssemblyTitleAttribute(string title) => Title = title;
        public string Title { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
    public sealed class AssemblyTrademarkAttribute : Attribute
    {
        public AssemblyTrademarkAttribute(string trademark) => Trademark = trademark;
        public string Trademark { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, Inherited = false)]
    public sealed class AssemblyVersionAttribute : Attribute
    {
        public AssemblyVersionAttribute(string version) => Version = version;
        public string Version { get; }
    }

    [Flags]
    public enum BindingFlags
    {
        Default = 0x00,
        IgnoreCase = 0x01,
        DeclaredOnly = 0x02,
        Instance = 0x04,
        Static = 0x08,
        Public = 0x10,
        NonPublic = 0x20,
        FlattenHierarchy = 0x40,
        InvokeMethod = 0x0100,
        CreateInstance = 0x0200,
        GetField = 0x0400,
        SetField = 0x0800,
        GetProperty = 0x1000,
        SetProperty = 0x2000,
        PutDispProperty = 0x4000,
        PutRefDispProperty = 0x8000,
        ExactBinding = 0x010000,
        SuppressChangeType = 0x020000,
        OptionalParamBinding = 0x040000,
        IgnoreReturn = 0x01000000,
        DoNotWrapExceptions = 0x02000000
    }

    [Flags]
    public enum CallingConventions
    {
        Standard = 0x0001,
        VarArgs = 0x0002,
        Any = Standard | VarArgs,
        HasThis = 0x0020,
        ExplicitThis = 0x0040
    }

    [Flags]
    public enum EventAttributes
    {
        None = 0x0000,
        SpecialName = 0x0200,
        RTSpecialName = 0x0400,
        ReservedMask = 0x0400
    }

    [Flags]
    public enum FieldAttributes
    {
        FieldAccessMask = 0x0007,
        PrivateScope = 0x0000,
        Private = 0x0001,
        FamANDAssem = 0x0002,
        Assembly = 0x0003,
        Family = 0x0004,
        FamORAssem = 0x0005,
        Public = 0x0006,
        Static = 0x0010,
        InitOnly = 0x0020,
        Literal = 0x0040,
        NotSerialized = 0x0080,
        SpecialName = 0x0200,
        PinvokeImpl = 0x2000,
        RTSpecialName = 0x0400,
        HasFieldMarshal = 0x1000,
        HasDefault = 0x8000,
        HasFieldRVA = 0x0100,
        ReservedMask = 0x9500
    }

    [Flags]
    public enum GenericParameterAttributes
    {
        None = 0x0000,
        VarianceMask = 0x0003,
        Covariant = 0x0001,
        Contravariant = 0x0002,
        SpecialConstraintMask = 0x001C,
        ReferenceTypeConstraint = 0x0004,
        NotNullableValueTypeConstraint = 0x0008,
        DefaultConstructorConstraint = 0x0010,
        AllowByRefLike = 0x0020
    }

    public enum ImageFileMachine
    {
        I386 = 0x014c,
        IA64 = 0x0200,
        AMD64 = 0x8664,
        ARM = 0x01c4
    }

    [Flags]
    public enum MemberTypes
    {
        Constructor = 0x01,
        Event = 0x02,
        Field = 0x04,
        Method = 0x08,
        Property = 0x10,
        TypeInfo = 0x20,
        Custom = 0x40,
        NestedType = 0x80,
        All = Constructor | Event | Field | Method | Property | TypeInfo | NestedType
    }

    [Flags]
    public enum MethodAttributes
    {
        MemberAccessMask = 0x0007,
        PrivateScope = 0x0000,
        Private = 0x0001,
        FamANDAssem = 0x0002,
        Assembly = 0x0003,
        Family = 0x0004,
        FamORAssem = 0x0005,
        Public = 0x0006,
        Static = 0x0010,
        Final = 0x0020,
        Virtual = 0x0040,
        HideBySig = 0x0080,
        CheckAccessOnOverride = 0x0200,
        VtableLayoutMask = 0x0100,
        ReuseSlot = 0x0000,
        NewSlot = 0x0100,
        Abstract = 0x0400,
        SpecialName = 0x0800,
        PinvokeImpl = 0x2000,
        UnmanagedExport = 0x0008,
        RTSpecialName = 0x1000,
        HasSecurity = 0x4000,
        RequireSecObject = 0x8000,
        ReservedMask = 0xd000
    }

    public enum MethodImplAttributes
    {
        CodeTypeMask = 0x0003,
        IL = 0x0000,
        Native = 0x0001,
        OPTIL = 0x0002,
        Runtime = 0x0003,
        ManagedMask = 0x0004,
        Unmanaged = 0x0004,
        Managed = 0x0000,
        ForwardRef = 0x0010,
        PreserveSig = 0x0080,
        InternalCall = 0x1000,
        Synchronized = 0x0020,
        NoInlining = 0x0008,
        AggressiveInlining = 0x0100,
        NoOptimization = 0x0040,
        AggressiveOptimization = 0x0200,
        Async = 0x2000,
        MaxMethodImplVal = 0xffff
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public sealed class ObfuscateAssemblyAttribute : Attribute
    {
        public ObfuscateAssemblyAttribute(bool assemblyIsPrivate) => AssemblyIsPrivate = assemblyIsPrivate;
        public bool AssemblyIsPrivate { get; }
        public bool StripAfterObfuscation { get; set; } = true;
    }

    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct |
        AttributeTargets.Method | AttributeTargets.Parameter | AttributeTargets.Field |
        AttributeTargets.Property | AttributeTargets.Event | AttributeTargets.Interface |
        AttributeTargets.Enum | AttributeTargets.Delegate, AllowMultiple = true, Inherited = false)]
    public sealed class ObfuscationAttribute : Attribute
    {
        public ObfuscationAttribute() { }
        public bool StripAfterObfuscation { get; set; } = true;
        public bool Exclude { get; set; } = true;
        public bool ApplyToMembers { get; set; } = true;
        public string? Feature { get; set; } = "all";
    }

    [Flags]
    public enum ParameterAttributes
    {
        None = 0x0000,
        In = 0x0001,
        Out = 0x0002,
        Lcid = 0x0004,
        Retval = 0x0008,
        Optional = 0x0010,
        HasDefault = 0x1000,
        HasFieldMarshal = 0x2000,
        Reserved3 = 0x4000,
        Reserved4 = 0x8000,
        ReservedMask = 0xf000
    }

    [Flags]
    public enum PortableExecutableKinds
    {
        NotAPortableExecutableImage = 0x0,
        ILOnly = 0x1,
        Required32Bit = 0x2,
        PE32Plus = 0x4,
        Unmanaged32Bit = 0x8,
        Preferred32Bit = 0x10
    }

    public enum ProcessorArchitecture
    {
        None = 0x0000,
        MSIL = 0x0001,
        X86 = 0x0002,
        IA64 = 0x0003,
        Amd64 = 0x0004,
        Arm = 0x0005
    }

    [Flags]
    public enum PropertyAttributes
    {
        None = 0x0000,
        SpecialName = 0x0200,
        RTSpecialName = 0x0400,
        HasDefault = 0x1000,
        Reserved2 = 0x2000,
        Reserved3 = 0x4000,
        Reserved4 = 0x8000,
        ReservedMask = 0xf400
    }

    [Flags]
    public enum ResourceAttributes
    {
        Public = 0x0001,
        Private = 0x0002
    }

    [Flags]
    public enum ResourceLocation
    {
        Embedded = 1,
        ContainedInAnotherAssembly = 2,
        ContainedInManifestFile = 4
    }

    [Flags]
    public enum TypeAttributes
    {
        VisibilityMask = 0x00000007,
        NotPublic = 0x00000000,
        Public = 0x00000001,
        NestedPublic = 0x00000002,
        NestedPrivate = 0x00000003,
        NestedFamily = 0x00000004,
        NestedAssembly = 0x00000005,
        NestedFamANDAssem = 0x00000006,
        NestedFamORAssem = 0x00000007,
        LayoutMask = 0x00000018,
        AutoLayout = 0x00000000,
        SequentialLayout = 0x00000008,
        ExplicitLayout = 0x00000010,
        ExtendedLayout = 0x00000018,
        ClassSemanticsMask = 0x00000020,
        Class = 0x00000000,
        Interface = 0x00000020,
        Abstract = 0x00000080,
        Sealed = 0x00000100,
        SpecialName = 0x00000400,
        Import = 0x00001000,
        Serializable = 0x00002000,
        WindowsRuntime = 0x00004000,
        StringFormatMask = 0x00030000,
        AnsiClass = 0x00000000,
        UnicodeClass = 0x00010000,
        AutoClass = 0x00020000,
        CustomFormatClass = 0x00030000,
        CustomFormatMask = 0x00C00000,
        BeforeFieldInit = 0x00100000,
        RTSpecialName = 0x00000800,
        HasSecurity = 0x00040000,
        ReservedMask = 0x00040800
    }
}
