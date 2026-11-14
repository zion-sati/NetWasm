// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Http.Json;

public static partial class HttpClientJsonExtensions
{
    public static Task<HttpResponseMessage> PostAsJsonAsync<T>(
        this HttpClient client,
        string? requestUri,
        T value,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default) =>
        PostAsJsonAsync(client, CreateUri(requestUri), value, jsonTypeInfo, cancellationToken);

    public static Task<HttpResponseMessage> PostAsJsonAsync<T>(
        this HttpClient client,
        Uri? requestUri,
        T value,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.PostAsync(requestUri!, JsonContent.Create(value, jsonTypeInfo), cancellationToken);
    }
}
