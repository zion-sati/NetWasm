// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Http.Json;

public static class HttpContentJsonExtensions
{
    public static Task<object?> ReadFromJsonAsync(
        this HttpContent content,
        Type type,
        JsonSerializerContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(context);
        return ReadFromJsonAsyncCore(content, type, context, cancellationToken);
    }

    public static Task<T?> ReadFromJsonAsync<T>(
        this HttpContent content,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        return ReadFromJsonAsyncCore(content, jsonTypeInfo, cancellationToken);
    }

    internal static async ValueTask<Stream> GetContentStreamAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        JsonHelpers.EnsureSupportedEncoding(content);
        return await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<object?> ReadFromJsonAsyncCore(
        HttpContent content,
        Type type,
        JsonSerializerContext context,
        CancellationToken cancellationToken)
    {
        using var stream = await GetContentStreamAsync(content, cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, type, context, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<T?> ReadFromJsonAsyncCore<T>(
        HttpContent content,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
    {
        using var stream = await GetContentStreamAsync(content, cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo, cancellationToken)
            .ConfigureAwait(false);
    }
}
