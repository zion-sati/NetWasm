// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Text.Json.Serialization.Converters
{
    internal sealed class JsonElementConverter : JsonConverter<JsonElement>
    {
        public JsonElementConverter()
        {
            // NetWasm intentionally does not identify framework converters by reflection.
            // This converter is part of this assembly's built-in converter set.
            IsInternalConverter = true;
            CanUseDirectReadOrWrite = true;
        }

        public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return JsonElement.ParseValue(ref reader, options.AllowDuplicateProperties);
        }

        public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options)
        {
            value.WriteTo(writer);
        }
    }
}
