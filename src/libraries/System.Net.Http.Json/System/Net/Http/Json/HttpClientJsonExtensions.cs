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

public static partial class HttpClientJsonExtensions
{
    private static readonly TimeSpan InfiniteTimeout = TimeSpan.FromMilliseconds(-1);
    private static readonly Func<HttpClient, Uri?, CancellationToken, Task<HttpResponseMessage>> s_getAsync =
        static (client, uri, cancellationToken) =>
            client.GetAsync(uri!, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

    public static Task<object?> GetFromJsonAsync(
        this HttpClient client,
        string? requestUri,
        Type type,
        JsonSerializerContext context,
        CancellationToken cancellationToken = default) =>
        GetFromJsonAsync(client, CreateUri(requestUri), type, context, cancellationToken);

    public static Task<object?> GetFromJsonAsync(
        this HttpClient client,
        Uri? requestUri,
        Type type,
        JsonSerializerContext context,
        CancellationToken cancellationToken = default) =>
        FromJsonAsyncCore(s_getAsync, client, requestUri, type, context, cancellationToken);

    public static Task<T?> GetFromJsonAsync<T>(
        this HttpClient client,
        string? requestUri,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default) =>
        GetFromJsonAsync(client, CreateUri(requestUri), jsonTypeInfo, cancellationToken);

    public static Task<T?> GetFromJsonAsync<T>(
        this HttpClient client,
        Uri? requestUri,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default) =>
        FromJsonAsyncCore(s_getAsync, client, requestUri, jsonTypeInfo, cancellationToken);

    private static Task<object?> FromJsonAsyncCore(
        Func<HttpClient, Uri?, CancellationToken, Task<HttpResponseMessage>> getMethod,
        HttpClient client,
        Uri? requestUri,
        Type type,
        JsonSerializerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(context);
        return FromJsonAsyncCore<object?, (Type, JsonSerializerContext)>(
            getMethod,
            client,
            requestUri,
            static (stream, state, token) => JsonSerializer.DeserializeAsync(stream, state.Item1, state.Item2, token),
            (type, context),
            cancellationToken);
    }

    private static Task<T?> FromJsonAsyncCore<T>(
        Func<HttpClient, Uri?, CancellationToken, Task<HttpResponseMessage>> getMethod,
        HttpClient client,
        Uri? requestUri,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        return FromJsonAsyncCore<T, JsonTypeInfo<T>>(
            getMethod,
            client,
            requestUri,
            static (stream, typeInfo, token) => JsonSerializer.DeserializeAsync(stream, typeInfo, token),
            jsonTypeInfo,
            cancellationToken);
    }

    private static async Task<TResult?> FromJsonAsyncCore<TResult, TOptions>(
        Func<HttpClient, Uri?, CancellationToken, Task<HttpResponseMessage>> getMethod,
        HttpClient client,
        Uri? requestUri,
        Func<Stream, TOptions, CancellationToken, ValueTask<TResult?>> deserialize,
        TOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var linkedCts = client.Timeout == InfiniteTimeout
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (linkedCts is not null)
        {
            linkedCts.CancelAfter(client.Timeout);
        }
        CancellationToken operationToken = linkedCts?.Token ?? cancellationToken;

        try
        {
            using var response = await getMethod(client, requestUri, operationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            // A response's content is never null: without a body it is empty content.
            var content = response.Content;
            var contentLength = content.Headers.ContentLength;
            if (contentLength is long length && length > client.MaxResponseContentBufferSize)
            {
                throw new HttpRequestException(
                    "The response content exceeded the configured buffer limit.");
            }

            var stream = await HttpContentJsonExtensions
                .GetContentStreamAsync(content, operationToken)
                .ConfigureAwait(false);
            using var limited = new LengthLimitReadStream(stream, client.MaxResponseContentBufferSize);
            return await deserialize(limited, options, operationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (linkedCts?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
        {
            throw new TaskCanceledException(
                $"The request timed out after {client.Timeout.TotalSeconds} seconds.",
                new TimeoutException(exception.Message, exception),
                exception.CancellationToken);
        }
    }

    private static Uri? CreateUri(string? requestUri) =>
        string.IsNullOrEmpty(requestUri) ? null : new Uri(requestUri!, UriKind.RelativeOrAbsolute);
}
