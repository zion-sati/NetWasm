namespace System.Runtime.InteropServices.WebAssembly
{
    public readonly struct WitUnit
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class WitImportAttribute : Attribute
    {
        // A function of the world itself, rather than of one of its interfaces.
        public WitImportAttribute(string functionName)
        {
            InterfaceName = string.Empty;
            FunctionName = functionName;
        }

        public WitImportAttribute(string interfaceName, string functionName)
        {
            InterfaceName = interfaceName;
            FunctionName = functionName;
        }

        public string InterfaceName { get; }
        public string FunctionName { get; }
    }

    // A Component Model built-in such as waitable-set.wait or a stream's
    // stream.write, which the canonical ABI defines rather than a WIT function.
    // The module is `$root`, `[export]` and an interface, or the interface at its
    // full version whose function the stream or future belongs to; the name is
    // the built-in's bracketed core name. The extern's parameters and result are
    // the built-in's core values.
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ComponentBuiltInAttribute : Attribute
    {
        public ComponentBuiltInAttribute(string moduleName, string functionName)
        {
            ModuleName = moduleName;
            FunctionName = functionName;
        }

        public string ModuleName { get; }
        public string FunctionName { get; }
    }

    // A core export under its full canonical name, such as a callback-lifted
    // export's `[async-lift]` start or its `[callback][async-lift]` callback,
    // which wit-component's standard names cannot express. The method's
    // parameters and result are the export's core values.
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ComponentExportAttribute : Attribute
    {
        public ComponentExportAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class WitExportAttribute : Attribute
    {
        // A function of the world itself, rather than of one of its interfaces.
        public WitExportAttribute(string functionName)
        {
            InterfaceName = string.Empty;
            FunctionName = functionName;
        }

        public WitExportAttribute(string interfaceName, string functionName)
        {
            InterfaceName = interfaceName;
            FunctionName = functionName;
        }

        public string InterfaceName { get; }
        public string FunctionName { get; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class WitPostReturnAttribute : Attribute
    {
        // A function of the world itself, rather than of one of its interfaces.
        public WitPostReturnAttribute(string functionName)
        {
            InterfaceName = string.Empty;
            FunctionName = functionName;
        }

        public WitPostReturnAttribute(string interfaceName, string functionName)
        {
            InterfaceName = interfaceName;
            FunctionName = functionName;
        }

        public string InterfaceName { get; }
        public string FunctionName { get; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class WitResourceAttribute : Attribute
    {
        // A resource the world itself declares, rather than one of its interfaces.
        public WitResourceAttribute(string resourceName)
        {
            InterfaceName = string.Empty;
            ResourceName = resourceName;
        }

        public WitResourceAttribute(string interfaceName, string resourceName)
        {
            InterfaceName = interfaceName;
            ResourceName = resourceName;
        }

        public string InterfaceName { get; }
        public string ResourceName { get; }
    }

    public readonly struct WitOption<T>
    {
        private readonly T _value;

        public WitOption(T value)
        {
            _value = value;
            HasValue = true;
        }

        public bool HasValue { get; }

        public T Value => HasValue
            ? _value
            : throw new InvalidOperationException();

        public static WitOption<T> None => default;
    }

    public readonly struct WitResult<TOk, TError>
    {
        private readonly TOk _ok;
        private readonly TError _error;

        private WitResult(TOk ok)
        {
            _ok = ok;
            _error = default!;
            IsOk = true;
        }

        private WitResult(TError error, bool _)
        {
            _ok = default!;
            _error = error;
            IsOk = false;
        }

        public bool IsOk { get; }

        public TOk Ok => IsOk ? _ok : throw new InvalidOperationException();

        public TError Error => !IsOk ? _error : throw new InvalidOperationException();

        public static WitResult<TOk, TError> FromOk(TOk value) => new(value);

        public static WitResult<TOk, TError> FromError(TError value) => new(value, false);
    }
}
