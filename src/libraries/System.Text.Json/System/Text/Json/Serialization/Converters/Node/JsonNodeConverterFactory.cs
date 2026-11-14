// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace System.Text.Json.Serialization.Converters
{
    internal sealed class JsonNodeConverterFactory : JsonConverterFactory
    {
        private static readonly JsonArrayConverter s_arrayConverter = new JsonArrayConverter();
        private static readonly JsonObjectConverter s_objectConverter = new JsonObjectConverter();
        private static readonly JsonValueConverter s_valueConverter = new JsonValueConverter();

        public JsonNodeConverterFactory()
        {
            // NetWasm intentionally does not identify framework converters by reflection.
            // This converter is part of this assembly's built-in converter set.
            IsInternalConverter = true;
        }

        public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            if (typeToConvert == typeof(JsonValue))
            {
                return s_valueConverter;
            }

            if (typeof(JsonObject) == typeToConvert)
            {
                return s_objectConverter;
            }

            if (typeof(JsonArray) == typeToConvert)
            {
                return s_arrayConverter;
            }

            Debug.Assert(typeof(JsonNode) == typeToConvert);
            return JsonNodeConverter.Instance;
        }

        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert == typeof(JsonNode) ||
            typeToConvert == typeof(JsonObject) ||
            typeToConvert == typeof(JsonArray) ||
            typeToConvert == typeof(JsonValue);
    }
}
