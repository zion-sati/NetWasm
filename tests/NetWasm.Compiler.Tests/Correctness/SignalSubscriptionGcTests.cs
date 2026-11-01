namespace NetWasm.Compiler.Tests.Correctness;

public sealed class SignalSubscriptionGcTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void StaticSignalSubscribersSurviveCollections() =>
        runner.Run(new("SignalSubscriptionRoots", "SignalSubscriptionRoots", Source, [42])
        {
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            CollectsAtEveryAllocation = true,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        using System.Collections.Generic;
        namespace SignalSubscriptionRoots;

        public sealed class Signal<T>(T initial)
        {
            private sealed class Subscription(Signal<T> signal, Action<T> handler) : IDisposable
            {
                private Signal<T>? owner = signal;

                public void Dispose()
                {
                    if (owner != null)
                    {
                        owner.Changed -= handler;
                        owner = null;
                    }
                }
            }

            private T current = initial;

            public event Action<T>? Changed;

            public T Value => current;

            public bool Set(T next)
            {
                if (EqualityComparer<T>.Default.Equals(current, next)) return false;
                current = next;
                Changed?.Invoke(next);
                return true;
            }

            public IDisposable Subscribe(Action<T> handler)
            {
                Changed += handler;
                return new Subscription(this, handler);
            }
        }

        public sealed class Theme(string name)
        {
            public string Name { get; } = name;
            public static Theme Dark { get; } = new("dark");
            public static Theme Light { get; } = new("light");
            public static Signal<Theme> Active { get; } = new(Dark);
        }

        public sealed class Text
        {
            public static int Changes;
            private readonly List<IDisposable> subscriptions = [];
            public string Content;

            public Text(string content)
            {
                Content = content;
                subscriptions.Add(Theme.Active.Subscribe(OnThemeChanged));
            }

            private void OnThemeChanged(Theme theme) => Changes += theme.Name.Length + Content.Length;

            public void Dispose()
            {
                foreach (var subscription in subscriptions) subscription.Dispose();
            }
        }

        public static class EntryPoint
        {
            public static int Trace() => 0;

            public static int Run(int input)
            {
                var texts = new List<Text>();
                for (var index = 0; index < 300; index++) texts.Add(new Text(new string('t', index % 7 + 1)));
                Theme.Active.Set(Theme.Light);
                var expected = 0;
                foreach (var text in texts) expected += 5 + text.Content.Length;
                if (Text.Changes != expected) return 1;
                for (var index = 0; index < texts.Count; index += 3) texts[index].Dispose();
                Text.Changes = 0;
                Theme.Active.Set(Theme.Dark);
                expected = 0;
                for (var index = 0; index < texts.Count; index++)
                    if (index % 3 != 0) expected += 4 + texts[index].Content.Length;
                return Text.Changes == expected ? input : 2;
            }
        }
        """;
}
