// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// Adapted from dotnet/runtime System.ComponentModel.DefaultValueAttribute.
// The upstream implementation is licensed under MIT.

using System.Diagnostics.CodeAnalysis;

namespace System.ComponentModel
{
    /// <summary>Specifies the default value for a property.</summary>
    [AttributeUsage(AttributeTargets.All)]
    public class DefaultValueAttribute : Attribute
    {
        private object? _value;
        private static readonly object s_throwSentinel = new();

        internal static bool IsSupported => false;

        public DefaultValueAttribute(Type type, string? value)
        {
            _value = s_throwSentinel;
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
        [CLSCompliant(false)]
        public DefaultValueAttribute(sbyte value) => _value = value;
        [CLSCompliant(false)]
        public DefaultValueAttribute(ushort value) => _value = value;
        [CLSCompliant(false)]
        public DefaultValueAttribute(uint value) => _value = value;
        [CLSCompliant(false)]
        public DefaultValueAttribute(ulong value) => _value = value;

        public virtual object? Value
        {
            get
            {
                if (ReferenceEquals(_value, s_throwSentinel))
                {
                    throw new ArgumentException(
                        "Runtime instantiation of this attribute is not allowed.");
                }

                return _value;
            }
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (obj == this)
            {
                return true;
            }

            if (obj is not DefaultValueAttribute other)
            {
                return false;
            }

            return Value is null ? other.Value is null : Value.Equals(other.Value);
        }

        public override int GetHashCode() => _value?.GetHashCode() ?? 0;

        protected void SetValue(object? value) => _value = value;
    }
}
