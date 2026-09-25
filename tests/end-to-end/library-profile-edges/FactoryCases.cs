using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Fixtures.LibraryProfileEdges;

public static class FactoryCases
{
    public static async Task LiveLeasesAcrossExpiry()
    {
        var handlers = new List<PendingHandler>();
        var services = new ServiceCollection();
        services.AddHttpClient("leased", client =>
        {
            client.BaseAddress = new Uri("https://lease.test/root/");
            client.Timeout = TimeSpan.FromMilliseconds(-1);
        }).SetHandlerLifetime(TimeSpan.FromSeconds(1))
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new PendingHandler();
                handlers.Add(handler);
                return handler;
            });
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var first = factory.CreateClient("leased");
        using var second = factory.CreateClient("leased");
        Program.Require(handlers.Count == 1);
        var pending = first.GetAsync("items");
        Program.Require(!pending.IsCompleted && handlers[0].Uri == "https://lease.test/root/items");
        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        using var replacement = factory.CreateClient("leased");
        Program.Require(handlers.Count == 2 && handlers[0].Disposals == 0 && !pending.IsCompleted);
        handlers[0].Respond();
        using var response = await pending;
        Program.Require(response.StatusCode == HttpStatusCode.OK);
        first.Dispose();
        Program.Require(handlers[0].Disposals == 0);
        using var stillLive = await second.GetAsync("other");
        Program.Require(stillLive.StatusCode == HttpStatusCode.OK && handlers[0].Uri == "https://lease.test/root/other");
        second.Dispose();
        using var cleanupTrigger = factory.CreateClient("leased");
#if NETWASM
        // Operation-driven release is a documented NetWasm profile contract;
        // desktop's periodic weak-reference cleanup has no immediate deadline.
        Program.Require(handlers[0].Disposals == 1);
        second.Dispose();
        Program.Require(handlers[0].Disposals == 1);
#endif
        Program.Require(handlers[1].Disposals == 0);
    }

    public static async Task NamedPendingCancellation()
    {
        var handler = new PendingHandler();
        var services = new ServiceCollection();
        services.AddHttpClient("cancel", client => client.Timeout = TimeSpan.FromMilliseconds(-1))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("cancel");
        using var cancellation = new CancellationTokenSource();
        var pending = client.GetAsync("https://cancel.test/items", cancellation.Token);
        Program.Require(handler.Started && !pending.IsCompleted && handler.Disposals == 0);
        cancellation.Cancel();
        var caught = false;
        try { await pending; }
        catch (OperationCanceledException exception)
        {
            caught = exception.CancellationToken == cancellation.Token;
        }
        Program.Require(caught && pending.IsCanceled && handler.Disposals == 0);
    }

    private sealed class PendingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<bool> _response = new();
        public int Disposals;
        public bool Started;
        public string? Uri;
        public void Respond() => _response.TrySetResult(true);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started = true;
            Uri = request.RequestUri!.AbsoluteUri;
            using var registration = cancellationToken.Register(() => _response.TrySetCanceled(cancellationToken));
            await _response.Task;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposals++;
            base.Dispose(disposing);
        }
    }
}
