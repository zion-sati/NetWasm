// Adapted from dotnet/runtime System serialization metadata declarations.
// The upstream implementation is licensed under MIT.

using System.ComponentModel;

namespace System
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Delegate, Inherited = false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class SerializableAttribute : Attribute
    {
        public SerializableAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class NonSerializedAttribute : Attribute
    {
        public NonSerializedAttribute() { }
    }
}
