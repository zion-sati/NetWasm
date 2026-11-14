// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Text.Json.Serialization.Converters
{
#if !NETWASM
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.Text.Json.Reflection;

    [RequiresDynamicCode(JsonSerializer.SerializationRequiresDynamicCodeMessage)]
    internal sealed class EnumConverterFactory : JsonConverterFactory
    {
        public EnumConverterFactory()
        {
        }

        public override bool CanConvert(Type type)
        {
            return type.IsEnum;
        }

        public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options)
        {
            Debug.Assert(CanConvert(type));
            return Create(type, EnumConverterOptions.AllowNumbers, namingPolicy: null, options);
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2071:UnrecognizedReflectionPattern",
            Justification = "'EnumConverter<T> where T : struct' implies 'T : new()', so the trimmer is warning calling MakeGenericType here because enumType's constructors are not annotated. " +
            "But EnumConverter doesn't call new T(), so this is safe.")]
        public static JsonConverter Create(Type enumType, EnumConverterOptions converterOptions, JsonNamingPolicy? namingPolicy, JsonSerializerOptions options)
        {
            if (!Helpers.IsSupportedTypeCode(Type.GetTypeCode(enumType)))
            {
                // Char-backed enums are valid in IL and F# but are not supported by System.Text.Json.
                return UnsupportedTypeConverterFactory.CreateUnsupportedConverterForType(enumType);
            }

            Type converterType = typeof(EnumConverter<>).MakeGenericType(enumType);
            return (JsonConverter)converterType.CreateInstanceNoWrapExceptions(
                parameterTypes: [typeof(EnumConverterOptions), typeof(JsonNamingPolicy), typeof(JsonSerializerOptions)],
                parameters: [converterOptions, namingPolicy, options])!;
        }
    }
#endif

    // The generic path is used by generated metadata and does not inspect Type or
    // construct converters dynamically. Keep the upstream helper algorithm in the
    // closed-world NetWasm profile.
    internal static class EnumConverterFactory
    {
        internal static class Helpers
        {
            public static TypeCode GetTypeCode<T>() where T : struct, Enum
            {
                Type underlyingType = Enum.GetUnderlyingType(typeof(T));
                if (underlyingType == typeof(sbyte)) return TypeCode.SByte;
                if (underlyingType == typeof(byte)) return TypeCode.Byte;
                if (underlyingType == typeof(short)) return TypeCode.Int16;
                if (underlyingType == typeof(ushort)) return TypeCode.UInt16;
                if (underlyingType == typeof(int)) return TypeCode.Int32;
                if (underlyingType == typeof(uint)) return TypeCode.UInt32;
                if (underlyingType == typeof(long)) return TypeCode.Int64;
                if (underlyingType == typeof(ulong)) return TypeCode.UInt64;
                return TypeCode.Char;
            }

            public static bool IsSupportedTypeCode(TypeCode typeCode)
            {
                return typeCode is TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64
                                or TypeCode.Byte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64;
            }

            public static JsonConverter<T> Create<T>(EnumConverterOptions converterOptions, JsonSerializerOptions options, JsonNamingPolicy? namingPolicy = null)
                where T : struct, Enum
            {
                if (!IsSupportedTypeCode(GetTypeCode<T>()))
                {
                    // Char-backed enums are valid in IL and F# but are not supported by System.Text.Json.
                    return new UnsupportedTypeConverter<T>();
                }

                return new EnumConverter<T>(converterOptions, namingPolicy, options);
            }
        }
    }
}
