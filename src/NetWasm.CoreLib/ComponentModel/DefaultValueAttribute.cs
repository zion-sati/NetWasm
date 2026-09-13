// Adapted from dotnet/runtime System.ComponentModel.DefaultValueAttribute.
// The upstream implementation is licensed under MIT.

namespace System.ComponentModel
{
    /// <summary>Specifies the default value for a property.</summary>
    /// <remarks>
    /// The Type/string overload is retained as a metadata carrier.  Resolving a
    /// TypeConverter would be executable reflection and is intentionally outside
    /// the supported CoreLib profile.
    /// </remarks>
    [AttributeUsage(AttributeTargets.All)]
    public class DefaultValueAttribute : Attribute
    {
        private readonly object? _value;

        public DefaultValueAttribute(Type type, string? value)
        {
            _value = value;
        }

        public DefaultValueAttribute(char value) => _value = value;
        public DefaultValueAttribute(byte value) => _value = value;
        public DefaultValueAttribute(short value) => _value = value;
        public DefaultValueAttribute(int value) => _value = value;
        public DefaultValueAttribute(long value) => _value = value;
        public DefaultValueAttribute(float value) => _value = value;
        public DefaultValueAttribute(double value) => _value = value;
        public DefaultValueAttribute(bool value) => _value = value;
        public DefaultValueAttribute(string? value) => _value = value;
        public DefaultValueAttribute(object? value) => _value = value;
        public DefaultValueAttribute(sbyte value) => _value = value;
        public DefaultValueAttribute(ushort value) => _value = value;
        public DefaultValueAttribute(uint value) => _value = value;
        public DefaultValueAttribute(ulong value) => _value = value;

        public virtual object? Value
        {
            get => _value;
        }

        public override bool Equals(object? obj) =>
            obj is DefaultValueAttribute other && object.Equals(_value, other._value);

        public override int GetHashCode() => _value?.GetHashCode() ?? 0;
    }
}
