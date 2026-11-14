// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    internal static class Obsoletions
    {
        internal const string SharedUrlFormat = "https://aka.ms/dotnet-warnings/{0}";

        internal const string JsonSerializerOptionsIgnoreNullValuesMessage =
            "JsonSerializerOptions.IgnoreNullValues is obsolete. To ignore null values when serializing, set DefaultIgnoreCondition to JsonIgnoreCondition.WhenWritingNull.";
        internal const string JsonSerializerOptionsIgnoreNullValuesDiagId = "SYSLIB0020";

        internal const string JsonSerializerOptionsAddContextMessage =
            "JsonSerializerOptions.AddContext is obsolete. To register a JsonSerializerContext, use either the TypeInfoResolver or TypeInfoResolverChain properties.";
        internal const string JsonSerializerOptionsAddContextDiagId = "SYSLIB0049";
    }
}
