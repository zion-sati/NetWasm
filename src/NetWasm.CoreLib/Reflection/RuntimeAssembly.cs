namespace System.Reflection
{
    // NetWasm: one Assembly per assembly whose types the program has, told apart by the index the
    // runtime type table gives each type's assembly. Its name comes from the type-symbols sidecar;
    // without the sidecar it reads as unavailable.
    internal sealed class RuntimeAssembly : Assembly
    {
        private static RuntimeAssembly?[]? s_assemblies;

        private readonly int _index;
        private RuntimeModule? _module;

        private RuntimeAssembly(int index) => _index = index;

        // The assembly with the table's index, or null for index 0.
        internal static Assembly? FromIndex(int index)
        {
            if (index == 0)
            {
                return null;
            }
            var assemblies = s_assemblies ??= new RuntimeAssembly?[RuntimeTypeTable.AssemblyCapacity()];
            return assemblies[index] ??= new RuntimeAssembly(index);
        }

        // Assembly.GetExecutingAssembly in a method of TDeclaring; the compiler calls this in its place.
        internal static Assembly Of<TDeclaring>() => typeof(TDeclaring).Assembly;

        public override string? FullName => DisplayName ?? TypeSymbols.AssemblyNameUnavailable;

        // One module per assembly, as NativeAOT has.
        public override Module ManifestModule => _module ??= new RuntimeModule(this);

        public override Module[] GetModules(bool getResourceModules) => [ManifestModule];

        public override Module[] GetLoadedModules(bool getResourceModules) => [ManifestModule];

        public override Module? GetModule(string name)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            return name == ManifestModule.ScopeName ? ManifestModule : null;
        }

        // The program's type definitions in this assembly, in type ID order, from the type table; one
        // nothing in the program uses is not there.
        public override Type[] GetTypes() => Types(visibleOnly: false);

        public override Type[] GetExportedTypes() => Types(visibleOnly: true);

        private Type[] Types(bool visibleOnly)
        {
            int[] ids = new int[RuntimeTypeTable.Capacity()];
            int count = 0;
            for (int id = 1; id < ids.Length; id++)
            {
                if ((RuntimeTypeTable.Flags(id) & RuntimeTypeFactsFlags.TypeDefinition) != 0 && RuntimeTypeTable.Assembly(id) == _index &&
                    (!visibleOnly || Type.GetTypeFromSemanticId(id).IsVisible))
                {
                    ids[count++] = id;
                }
            }
            return Type.TypesOf(ids, count);
        }

        public override Type? GetType(string name, bool throwOnError, bool ignoreCase)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            return Type.FindByName(name, throwOnError, ignoreCase, this);
        }

        // As a NativeAOT or single-file assembly answers: no file, no load context, never reflection-only.
        public override string Location => string.Empty;

        public override string ImageRuntimeVersion => "v4.0.30319";

        public override bool ReflectionOnly => false;

        public override bool GlobalAssemblyCache => false;

        public override long HostContext => 0;

        public override bool IsCollectible => false;

        public override AssemblyName GetName(bool copiedName) =>
            DisplayName is { } name ? new AssemblyName(name) : base.GetName(copiedName);

        // The sidecar's display name (Name, Version=..., Culture=..., PublicKeyToken=...), in the entry just
        // past the type IDs by the assembly's index.
        private string? DisplayName => TypeSymbols.Find(RuntimeTypeTable.Capacity() + _index)?.DisplayName;
    }

    // NetWasm: an assembly's one module, as NativeAOT's: its file and name are unknown, its scope name is
    // the assembly's file name where the type-symbols sidecar names the assembly, and its types are the
    // assembly's.
    internal sealed class RuntimeModule(Assembly assembly) : Module
    {
        private const string Unknown = "<Unknown>";

        public override Assembly Assembly => assembly;

        public override string FullyQualifiedName => Unknown;

        public override string Name => Unknown;

        public override string ScopeName =>
            assembly.GetName().Name is { } name && name != TypeSymbols.AssemblyNameUnavailable ? name + ".dll" : Unknown;

        public override int MetadataToken =>
            throw new InvalidOperationException("There is no metadata token available for the given member.");

        public override Type? GetType(string className, bool throwOnError, bool ignoreCase) =>
            assembly.GetType(className, throwOnError, ignoreCase);

        public override Type[] GetTypes() => assembly.GetTypes();

        public override bool Equals(object? obj) => obj is RuntimeModule other && other.Assembly.Equals(Assembly);

        public override int GetHashCode() => Assembly.GetHashCode();
    }
}
