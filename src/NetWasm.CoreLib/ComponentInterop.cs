namespace System.Runtime.InteropServices.WebAssembly
{
    public readonly struct WitUnit
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class WitImportAttribute : Attribute
    {
        public WitImportAttribute(string interfaceName, string functionName)
        {
            InterfaceName = interfaceName;
            FunctionName = functionName;
        }

        public string InterfaceName { get; }
        public string FunctionName { get; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class WitExportAttribute : Attribute
    {
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
