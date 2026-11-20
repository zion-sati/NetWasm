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

// Upstream's metadata overload of ReadFromJsonAsAsyncEnumerable (HttpContentJsonExtensions.AsyncEnumerable.cs at
// dotnet/runtime deae2e32).
public static partial class HttpContentJsonExtensions
{
    public static IAsyncEnumerable<T?> ReadFromJsonAsAsyncEnumerable<T>(
        this HttpContent content,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        return ReadFromJsonAsAsyncEnumerableCore(content, jsonTypeInfo, cancellationToken);
    }

    private static async IAsyncEnumerable<T?> ReadFromJsonAsAsyncEnumerableCore<T>(
        HttpContent content,
        JsonTypeInfo<T> jsonTypeInfo,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stream = await GetContentStreamAsync(content, cancellationToken).ConfigureAwait(false);
        await foreach (var value in JsonSerializer.DeserializeAsyncEnumerable(stream, jsonTypeInfo, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return value;
        }
    }
}
