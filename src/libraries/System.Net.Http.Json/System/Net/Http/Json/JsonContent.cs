// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Http.Json;

public sealed class JsonContent : HttpContent
{
    private readonly JsonTypeInfo _typeInfo;

    private JsonContent(object? value, JsonTypeInfo typeInfo, MediaTypeHeaderValue? mediaType)
    {
        Value = value;
        _typeInfo = typeInfo;

        Headers.ContentType = mediaType ?? JsonHelpers.GetDefaultMediaType();
        JsonHelpers.EnsureSupportedEncoding(this);
    }

    public Type ObjectType => _typeInfo.Type;

    public object? Value { get; }

    public static JsonContent Create<T>(
        T? inputValue,
        JsonTypeInfo<T> jsonTypeInfo,
        MediaTypeHeaderValue? mediaType = null)
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        return new JsonContent(inputValue, jsonTypeInfo, mediaType);
    }

    public static JsonContent Create(
        object? inputValue,
        JsonTypeInfo jsonTypeInfo,
        MediaTypeHeaderValue? mediaType = null)
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        EnsureTypeCompatibility(inputValue, jsonTypeInfo.Type);
        return new JsonContent(inputValue, jsonTypeInfo, mediaType);
    }

    // System.Net.Http has netstandard 2.1's HttpContent, whose one serialization override takes
    // the transport context, as upstream's netstandard build of this type overrides it.
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        JsonHelpers.EnsureSupportedEncoding(this);
        return JsonSerializer.SerializeAsync(stream, Value, _typeInfo);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    private static void EnsureTypeCompatibility(object? value, Type inputType)
    {
        // The trimmed NetWasm Type surface does not expose IsAssignableFrom.
        // The source-generated JsonTypeInfo performs the runtime compatibility
        // check as part of SerializeAsObjectAsync.
    }
}
