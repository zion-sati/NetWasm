using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace NetWasm.Fixtures.LibraryProfileEdges;

public static class LifetimeCases
{
    public static void ChangeTokenDisposal()
    {
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();
        using var third = new CancellationTokenSource();
        var registrations = 0;
        var callbacks = 0;
        var registration = ChangeToken.OnChange(() =>
        {
            registrations++;
            return new CancellationChangeToken(registrations switch
            {
                1 => first.Token,
                2 => second.Token,
                3 => third.Token,
                _ => throw new InvalidOperationException(),
            });
        }, () => callbacks++);
        Program.Require(registrations == 1 && callbacks == 0);
        first.Cancel();
        Program.Require(registrations == 2 && callbacks == 1);
        second.Cancel();
        Program.Require(registrations == 3 && callbacks == 2);
        registration.Dispose();
        registration.Dispose();
        third.Cancel();
        Program.Require(registrations == 3 && callbacks == 2);
    }

    public static void OptionsLifetime()
    {
        using var source = new Changes();
        var services = new ServiceCollection();
        services.AddSingleton<IOptionsChangeTokenSource<Settings>>(source);
        services.AddOptions<Settings>().Configure(value => value.Generation = source.Generation);
        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        var firstSnapshot = firstScope.ServiceProvider.GetRequiredService<IOptionsSnapshot<Settings>>();
        var original = firstSnapshot.Value;
        var monitor = provider.GetRequiredService<IOptionsMonitor<Settings>>();
        Program.Require(original.Generation == 1 && monitor.CurrentValue.Generation == 1);
        var callbacks = 0;
        var observed = 0;
        using var listener = monitor.OnChange((value, name) =>
        {
            Program.Require(name == Options.DefaultName);
            callbacks++;
            observed = value.Generation;
        });
        source.Signal();
        Program.Require(callbacks == 1 && observed == 2 && monitor.CurrentValue.Generation == 2);
        source.Signal();
        Program.Require(callbacks == 2 && observed == 3 && monitor.CurrentValue.Generation == 3);
        listener!.Dispose();
        source.Signal();
        Program.Require(callbacks == 2 && observed == 3 && monitor.CurrentValue.Generation == 4);
        Program.Require(ReferenceEquals(original, firstSnapshot.Value) && original.Generation == 1);
        using var secondScope = provider.CreateScope();
        var secondSnapshot = secondScope.ServiceProvider.GetRequiredService<IOptionsSnapshot<Settings>>();
        Program.Require(secondSnapshot.Value.Generation == 4 && !ReferenceEquals(original, secondSnapshot.Value));
    }

    public static async Task CacheLifetime()
    {
        var clock = new Clock();
        using var cache = new MemoryCache(Options.Create(new MemoryCacheOptions { Clock = clock }));
        cache.Set("expires", 42, TimeSpan.FromTicks(1));
        Program.Require(cache.Get<int>("expires") == 42);
        clock.UtcNow += TimeSpan.FromTicks(1);
        Program.Require(!cache.TryGetValue("expires", out _));

        var replaced = new TaskCompletionSource<bool>();
        var removed = new TaskCompletionSource<bool>();
        var replacedCount = 0;
        var removedCount = 0;
        cache.Set("key", "old", new MemoryCacheEntryOptions().RegisterPostEvictionCallback((key, value, reason, _) =>
        {
            replacedCount++;
            replaced.TrySetResult((string)key == "key" && (string?)value == "old" && reason == EvictionReason.Replaced);
        }));
        cache.Set("key", "new", new MemoryCacheEntryOptions().RegisterPostEvictionCallback((key, value, reason, _) =>
        {
            removedCount++;
            removed.TrySetResult((string)key == "key" && (string?)value == "new" && reason == EvictionReason.Removed);
        }));
        Program.Require(await replaced.Task);
        Program.Require(cache.Get<string>("key") == "new" && replacedCount == 1 && removedCount == 0);
        cache.Remove("key");
        Program.Require(await removed.Task);
        cache.Remove("key");
        Program.Require(replacedCount == 1 && removedCount == 1 && !cache.TryGetValue("key", out _));
        cache.Dispose();
        try
        {
            cache.TryGetValue("key", out _);
            throw new InvalidOperationException("Disposed cache remained usable.");
        }
        catch (ObjectDisposedException) { }
    }

    public sealed class Settings { public int Generation { get; set; } }

    private sealed class Changes : IOptionsChangeTokenSource<Settings>, IDisposable
    {
        private CancellationTokenSource _source = new();
        public int Generation { get; private set; } = 1;
        public string Name => Options.DefaultName;
        public IChangeToken GetChangeToken() => new CancellationChangeToken(_source.Token);
        public void Signal()
        {
            var previous = _source;
            _source = new CancellationTokenSource();
            Generation++;
            previous.Cancel();
            previous.Dispose();
        }
        public void Dispose() => _source.Dispose();
    }

    private sealed class Clock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
    }
}
