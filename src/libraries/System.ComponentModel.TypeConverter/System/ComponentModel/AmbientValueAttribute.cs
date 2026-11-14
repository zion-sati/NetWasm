// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.Design;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace System.ComponentModel
{
    /// <summary>
    /// Specifies the ambient value for a property. The ambient value is the value you
    /// can set into a property to make it inherit its ambient.
    /// </summary>
    [AttributeUsage(AttributeTargets.All)]
    public sealed class AmbientValueAttribute : Attribute
    {
        /// <summary>
        /// This is the default value.
        /// </summary>
        private object? _value;
        private static readonly object s_throwSentinel = new();

        /// <summary>
        /// Initializes a new instance of the <see cref='System.ComponentModel.AmbientValueAttribute'/> class, converting the
        /// specified value to the specified type, and using the U.S. English culture as the
        /// translation context.
        /// </summary>
        public AmbientValueAttribute(Type type, string value)
        {
            // Preserve upstream's unavailable-conversion sentinel path so metadata
            // construction works, while consuming the unavailable value fails clearly.
            _value = s_throwSentinel;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref='System.ComponentModel.AmbientValueAttribute'/> class using a Unicode
        /// character.
        /// </summary>
        public AmbientValueAttribute(char value)
        {
            _value = value;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref='System.ComponentModel.AmbientValueAttribute'/> class using an 8-bit unsigned
        /// integer.
        /// </summary>
        public AmbientValueAttribute(byte value)
        {
            _value = value;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref='System.ComponentModel.AmbientValueAttribute'/> class using a 16-bit signed
        /// integer.
        /// </summary>
        public AmbientValueAttribute(short value)
        {
            _value = value;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref='System.ComponentModel.AmbientValueAttribute'/> class using a 32-bit signed
        /// integer.
        /// </summary>
        public AmbientValueAttribute(int value)
        {
            _value = value;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref='System.ComponentModel.AmbientValueAttribute'/> class using a 64-bit signed
        /// integer.
        /// </summary>
        public AmbientValueAttribute(long value)
        {
            _value = value;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref='System.ComponentModel.AmbientValueAttribute'/> class using a
        /// single-precision floating point number.
        /// </summary>
        public AmbientValueAttribute(float value)
        {
            _value = value;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref='System.ComponentModel.AmbientValueAttribute'/> class using a
        /// double-precision floating point number.
        /// </summary>
        public AmbientValueAttribute(double value)
        {
            _value = value;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref='System.ComponentModel.AmbientValueAttribute'/> class using a <see cref='bool'/>
        /// value.
        /// </summary>
        public AmbientValueAttribute(bool value)
        {
            _value = value;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref='System.ComponentModel.AmbientValueAttribute'/> class using a <see cref='string'/>.
        /// </summary>
        public AmbientValueAttribute(string? value)
        {
            _value = value;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref='System.ComponentModel.AmbientValueAttribute'/>
        /// class.
        /// </summary>
        public AmbientValueAttribute(object? value)
        {
            _value = value;
        }

        /// <summary>
        /// Gets the ambient value of the property this attribute is bound to.
        /// </summary>
        public object? Value
        {
            get
            {
                if (ReferenceEquals(_value, s_throwSentinel))
                {
                    throw new PlatformNotSupportedException("AmbientValueAttribute type-name conversion requires unavailable TypeDescriptor converter discovery. Use a typed value constructor.");
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

            if (obj is AmbientValueAttribute other)
            {
                return Value != null ? Value.Equals(other.Value) : other.Value == null;
            }

            return false;
        }

        // Hash the known value instead of using reflective attribute field discovery.
        public override int GetHashCode() => Value?.GetHashCode() ?? 0;
    }
}
