// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

// System.Net.Http.Json's metadata overloads newer than its .NET Standard surface: DeleteFromJsonAsync,
// PatchAsJsonAsync, the response and content async enumerables, and JsonContent's synchronous serialization. The
// fixture builds its JsonTypeInfo through JsonMetadataServices, as generated code does, so no reflection is involved.
public sealed class HttpJsonModernSurfaceCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ModernHttpJsonSurfaceMatchesDesktopAcrossProfilesAndTargets() =>
        runner.Run(new(
            "HttpJsonModernSurfaceCompatibilityCorpus",
            "NetWasm.Correctness.HttpJsonModernSurface",
            Source,
            [0, 1, 2, 3, 4])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            // HttpClient's timeout timer waits on the platform's waitable set.
            RequiresReactor = true,
            ExpectedReturnValue = 100,
        });

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.IO;
        using System.Net;
        using System.Net.Http;
        using System.Net.Http.Json;
        using System.Text;
        using System.Text.Json;
        using System.Text.Json.Serialization.Metadata;
        using System.Threading;
        using System.Threading.Tasks;

        namespace NetWasm.Correctness.HttpJsonModernSurface;

        public static class EntryPoint
        {
            public static int Run(int input) => input switch
            {
                0 => Delete(),
                1 => Patch(),
                2 => ResponseEnumerable(),
                3 => ContentEnumerable(),
                4 => SynchronousJsonContent(),
                _ => -1,
            };

            public static int Trace() => 0;

            // A resolver as a source-generated context is one: it answers for int through JsonMetadataServices.
            private sealed class Resolver : IJsonTypeInfoResolver
            {
                public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) =>
                    type == typeof(int) ? JsonMetadataServices.CreateValueInfo<int>(options, JsonMetadataServices.Int32Converter) : null;
            }

            private static readonly JsonSerializerOptions s_options = new() { TypeInfoResolver = new Resolver() };

            private static JsonTypeInfo<int> IntInfo() => (JsonTypeInfo<int>)s_options.GetTypeInfo(typeof(int));

            private sealed class Handler(string body) : HttpMessageHandler
            {
                public string? Method;
                public string? Received;

                protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    Method = request.Method.Method;
                    Received = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json"),
                    };
                }
            }

            private static int Delete()
            {
                var handler = new Handler("42");
                using var client = new HttpClient(handler) { BaseAddress = new Uri("http://example.test/") };
                var value = client.DeleteFromJsonAsync("items/1", IntInfo(), CancellationToken.None).GetAwaiter().GetResult();
                if (value != 42 || handler.Method != "DELETE") return 1;
                var again = client.DeleteFromJsonAsync(new Uri("http://example.test/items/2"), IntInfo()).GetAwaiter().GetResult();
                if (again != 42) return 2;
                return 100;
            }

            private static int Patch()
            {
                var handler = new Handler("{}");
                using var client = new HttpClient(handler) { BaseAddress = new Uri("http://example.test/") };
                using var response = client.PatchAsJsonAsync("items/1", 7, IntInfo(), CancellationToken.None).GetAwaiter().GetResult();
                if (handler.Method != "PATCH" || handler.Received != "7" || response.StatusCode != HttpStatusCode.OK) return 1;
                try
                {
                    client.PatchAsJsonAsync("items/1", 7, (JsonTypeInfo<int>)null!).GetAwaiter().GetResult();
                    return 2;
                }
                catch (ArgumentNullException)
                {
                }
                return 100;
            }

            private static int ResponseEnumerable()
            {
                var handler = new Handler("[1, 2, 3, 4]");
                using var client = new HttpClient(handler) { BaseAddress = new Uri("http://example.test/") };
                var sum = 0;
                var count = 0;
                var enumerator = client.GetFromJsonAsAsyncEnumerable("items", IntInfo()).GetAsyncEnumerator();
                try
                {
                    while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
                    {
                        sum += enumerator.Current;
                        count++;
                    }
                }
                finally
                {
                    enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                if (sum != 10 || count != 4 || handler.Method != "GET") return 1;
                return 100;
            }

            private static int ContentEnumerable()
            {
                using var content = new StringContent("[5, 6]", Encoding.UTF8, "application/json");
                var values = new List<int>();
                var enumerator = content.ReadFromJsonAsAsyncEnumerable(IntInfo()).GetAsyncEnumerator();
                try
                {
                    while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult()) values.Add(enumerator.Current);
                }
                finally
                {
                    enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                if (values.Count != 2 || values[0] != 5 || values[1] != 6) return 1;
                return 100;
            }

            private static int SynchronousJsonContent()
            {
                using var content = JsonContent.Create(123, IntInfo());
                var buffer = new MemoryStream();
                content.CopyTo(buffer, null, CancellationToken.None);
                if (Encoding.UTF8.GetString(buffer.ToArray()) != "123") return 1;
                using var again = JsonContent.Create(456, IntInfo());
                using var reader = new StreamReader(again.ReadAsStream());
                if (reader.ReadToEnd() != "456") return 2;
                if (again.Headers.ContentType!.MediaType != "application/json") return 3;
                return 100;
            }
        }
        """;
}
