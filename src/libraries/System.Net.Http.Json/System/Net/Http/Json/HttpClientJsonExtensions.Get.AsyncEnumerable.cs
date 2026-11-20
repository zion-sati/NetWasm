// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Http.Json;

// Upstream's metadata overloads of GetFromJsonAsAsyncEnumerable (HttpClientJsonExtensions.Get.AsyncEnumerable.cs at
// dotnet/runtime deae2e32): the response is read as it arrives, within the client's response buffer limit.
public static partial class HttpClientJsonExtensions
{
    public static IAsyncEnumerable<T?> GetFromJsonAsAsyncEnumerable<T>(
        this HttpClient client,
        string? requestUri,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default) =>
        GetFromJsonAsAsyncEnumerable(client, CreateUri(requestUri), jsonTypeInfo, cancellationToken);

    public static IAsyncEnumerable<T?> GetFromJsonAsAsyncEnumerable<T>(
        this HttpClient client,
        Uri? requestUri,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        return Core(client, requestUri, jsonTypeInfo, cancellationToken);

        static async IAsyncEnumerable<T?> Core(
            HttpClient client,
            Uri? requestUri,
            JsonTypeInfo<T> jsonTypeInfo,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var response = await client.GetAsync(requestUri!, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var content = response.Content;
            if (content.Headers.ContentLength is long length && length > client.MaxResponseContentBufferSize)
            {
                throw new HttpRequestException("The response content exceeded the configured buffer limit.");
            }

            var stream = await HttpContentJsonExtensions.GetContentStreamAsync(content, cancellationToken)
                .ConfigureAwait(false);
            using var limited = new LengthLimitReadStream(stream, client.MaxResponseContentBufferSize);
            await foreach (var value in JsonSerializer.DeserializeAsyncEnumerable(limited, jsonTypeInfo, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return value;
            }
        }
    }
}
