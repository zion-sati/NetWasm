// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace System.Text.Json.Nodes
{
    [DebuggerDisplay("{ToJsonString(),nq}")]
    [DebuggerTypeProxy(typeof(JsonValue<>.DebugView))]
    internal abstract class JsonValue<TValue> : JsonValue
    {
        internal readonly TValue Value; // keep as a field for direct access to avoid copies

        protected JsonValue(TValue value, JsonNodeOptions? options) : base(options)
        {
            Debug.Assert(value is not null);
            Debug.Assert(value is not JsonElement or JsonElement { ValueKind: not JsonValueKind.Null });
            Debug.Assert(value is not JsonNode);
            Value = value;
        }

        public override T GetValue<T>()
        {
            // If no conversion is needed, just return the raw value.
            if (Value is T returnValue)
            {
                return returnValue;
            }

            // Currently we do not support other conversions.
            // Generics (and also boxing) do not support standard cast operators say from 'long' to 'int',
            //  so attempting to cast here would throw InvalidCastException.
            ThrowHelper.ThrowInvalidOperationException_NodeUnableToConvert(typeof(TValue), typeof(T));
            return default!;
        }

        public override bool TryGetValue<T>([NotNullWhen(true)] out T value)
        {
            // If no conversion is needed, just return the raw value.
            if (Value is T returnValue)
            {
                value = returnValue;
                return true;
            }

            // Currently we do not support other conversions.
            // Generics (and also boxing) do not support standard cast operators say from 'long' to 'int',
            //  so attempting to cast here would throw InvalidCastException.
            value = default!;
            return false;
        }

        /// <summary>
        /// Whether <typeparamref name="TValue"/> is a built-in type that admits primitive JsonValue representation.
        /// </summary>
        internal static bool TypeIsSupportedPrimitive => s_valueKind.HasValue;
        private static readonly JsonValueKind? s_valueKind = DetermineValueKindForType(typeof(TValue));

        /// <summary>
        /// Determines the JsonValueKind for the value of a built-in type.
        /// </summary>
        private protected static JsonValueKind DetermineValueKind(TValue value)
        {
            Debug.Assert(s_valueKind is not null, "Should only be invoked for types that are supported primitives.");

            if (value is bool boolean)
            {
                // Boolean requires special handling since kind varies by value.
                return boolean ? JsonValueKind.True : JsonValueKind.False;
            }

            return s_valueKind.Value;
        }

        /// <summary>
        /// Precomputes the JsonValueKind for a given built-in type where possible.
        /// </summary>
        private static JsonValueKind? DetermineValueKindForType(Type type)
        {
            // NetWasm intentionally does not expose Type.IsEnum/GetTypeCode or
            // other reflection-derived classification. Generic type identity is
            // enough for the closed primitive set and keeps JsonValue creation
            // reflection-free/AOT-friendly.
            if (type == typeof(bool) || type == typeof(bool?))
            {
                return JsonValueKind.Undefined; // Varies between true and false.
            }

            if (type == typeof(sbyte) || type == typeof(sbyte?) ||
                type == typeof(byte) || type == typeof(byte?) ||
                type == typeof(short) || type == typeof(short?) ||
                type == typeof(ushort) || type == typeof(ushort?) ||
                type == typeof(int) || type == typeof(int?) ||
                type == typeof(uint) || type == typeof(uint?) ||
                type == typeof(long) || type == typeof(long?) ||
                type == typeof(ulong) || type == typeof(ulong?) ||
                type == typeof(float) || type == typeof(float?) ||
                type == typeof(double) || type == typeof(double?) ||
                type == typeof(decimal) || type == typeof(decimal?))
            {
                return JsonValueKind.Number;
            }

            if (type == typeof(string) || type == typeof(char) || type == typeof(char?) ||
                type == typeof(DateTime) || type == typeof(DateTime?) ||
                type == typeof(DateTimeOffset) || type == typeof(DateTimeOffset?) ||
                type == typeof(TimeSpan) || type == typeof(TimeSpan?) ||
                type == typeof(Guid) || type == typeof(Guid?) ||
                type == typeof(Uri) || type == typeof(Version))
            {
                return JsonValueKind.String;
            }

            return null;
        }

        [ExcludeFromCodeCoverage] // Justification = "Design-time"
        [DebuggerDisplay("{Json,nq}")]
        private sealed class DebugView
        {
            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            public JsonValue<TValue> _node;

            public DebugView(JsonValue<TValue> node)
            {
                _node = node;
            }

            public string Json => _node.ToJsonString();
            public string Path => _node.GetPath();
            public TValue? Value => _node.Value;
        }
    }
}
