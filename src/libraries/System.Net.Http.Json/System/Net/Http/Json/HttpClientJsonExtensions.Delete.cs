// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Http.Json;

// Upstream's metadata overloads of DeleteFromJsonAsync (HttpClientJsonExtensions.Delete.cs at dotnet/runtime deae2e32).
public static partial class HttpClientJsonExtensions
{
    private static readonly Func<HttpClient, Uri?, CancellationToken, Task<HttpResponseMessage>> s_deleteAsync =
        static (client, uri, cancellationToken) => client.DeleteAsync(uri!, cancellationToken);

    public static Task<object?> DeleteFromJsonAsync(
        this HttpClient client,
        string? requestUri,
        Type type,
        JsonSerializerContext context,
        CancellationToken cancellationToken = default) =>
        DeleteFromJsonAsync(client, CreateUri(requestUri), type, context, cancellationToken);

    public static Task<object?> DeleteFromJsonAsync(
        this HttpClient client,
        Uri? requestUri,
        Type type,
        JsonSerializerContext context,
        CancellationToken cancellationToken = default) =>
        FromJsonAsyncCore(s_deleteAsync, client, requestUri, type, context, cancellationToken);

    public static Task<T?> DeleteFromJsonAsync<T>(
        this HttpClient client,
        string? requestUri,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default) =>
        DeleteFromJsonAsync(client, CreateUri(requestUri), jsonTypeInfo, cancellationToken);

    public static Task<T?> DeleteFromJsonAsync<T>(
        this HttpClient client,
        Uri? requestUri,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default) =>
        FromJsonAsyncCore(s_deleteAsync, client, requestUri, jsonTypeInfo, cancellationToken);
}
