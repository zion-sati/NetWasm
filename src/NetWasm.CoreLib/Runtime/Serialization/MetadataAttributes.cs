// Adapted from dotnet/runtime System.Runtime.Serialization metadata
// declarations. The upstream implementation is licensed under MIT.

namespace System.Runtime.Serialization
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class OnDeserializedAttribute : Attribute
    {
        public OnDeserializedAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class OnDeserializingAttribute : Attribute
    {
        public OnDeserializingAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class OnSerializedAttribute : Attribute
    {
        public OnSerializedAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class OnSerializingAttribute : Attribute
    {
        public OnSerializingAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class OptionalFieldAttribute : Attribute
    {
        private int _versionAdded = 1;

        public OptionalFieldAttribute() { }

        public int VersionAdded
        {
            get => _versionAdded;
            set
            {
                if (value < 1) throw new ArgumentException();
                _versionAdded = value;
            }
        }
    }
}
