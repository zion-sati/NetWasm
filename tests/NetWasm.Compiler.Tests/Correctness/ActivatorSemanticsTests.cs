namespace NetWasm.Compiler.Tests.Correctness;

// Activator.CreateInstance<T>() and the APIs built on it (new T(), Lazy<T>, LazyInitializer,
// ConditionalWeakTable.GetOrCreateValue) compile for every T and behave as on .NET: a type
// without a public parameterless constructor fails when the call runs, not when it compiles.
public sealed class ActivatorSemanticsTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    [Fact]
    public void CreateInstanceConstructsOrThrowsAsDotNetDoesForEveryKindOfType() => Run(new(
        "ActivatorSemantics", "NetWasm.Correctness.ActivatorSemantics",
        """
        using System;
        using System.Runtime.CompilerServices;
        using System.Threading;
        namespace NetWasm.Correctness.ActivatorSemantics;
        public abstract class Abstract { public Abstract() { } }
        public class PrivateConstructor { private PrivateConstructor() { } }
        public class ProtectedConstructor { protected ProtectedConstructor() { } }
        public class OnlyArguments { public OnlyArguments(int value) { } }
        public class Public { public int Value = 5; }
        public struct WithConstructor { public int Value; public WithConstructor() { Value = 7; } }
        public static class EntryPoint
        {
            public static int RunDesktop(int input) => Run(input);
            public static int Trace() => 0;
            private static int Hash(string text)
            {
                var hash = 17;
                foreach (var c in text) hash = unchecked(hash * 31 + c);
                return hash & 0x7fffffff;
            }
            // The failure's type, from the type itself: a program carries no type names, and a message
            // that names a type reads type#<id> (issue 225).
            private static int Failure(Exception failure) => Hash(failure switch
            {
                MissingMethodException => "System.MissingMethodException",
                MissingMemberException => "System.MissingMemberException",
                MemberAccessException => "System.MemberAccessException",
                PlatformNotSupportedException => "System.PlatformNotSupportedException",
                NotSupportedException => "System.NotSupportedException",
                ArgumentException => "System.ArgumentException",
                InvalidOperationException => "System.InvalidOperationException",
                _ => "other",
            });
            private static T Make<T>() where T : new() => new T();
            private static int Create<T>()
            {
                try { var value = Activator.CreateInstance<T>(); return value is null ? 1 : 2; }
                catch (Exception failure) { return Failure(failure); }
            }
            public static int Run(int input)
            {
                try
                {
                    switch (input)
                    {
                        case 0: return Create<string>();
                        case 1: return Create<Abstract>();
                        case 2: return Create<IDisposable>();
                        case 3: return Create<PrivateConstructor>();
                        case 4: return Create<ProtectedConstructor>();
                        case 5: return Create<OnlyArguments>();
                        case 6: return Create<int[]>();
                        case 7: return Create<Action>() ^ Create<Func<int>>() ^ Create<Delegate>() ^ Create<MulticastDelegate>();
                        case 8: return Activator.CreateInstance<int>() + 100;
                        case 9: return Activator.CreateInstance<int?>() is null ? 101 : -1;
                        case 10: return Activator.CreateInstance<WithConstructor>().Value + 100;
                        case 11: return Activator.CreateInstance<Public>().Value + 100;
                        case 12: return Make<Public>().Value + Make<WithConstructor>().Value + Make<int>() + 100;
                        case 13: return new Lazy<string>(() => "lazy").Value.Length + 100;
                        case 14:
                        {
                            var lazy = new Lazy<string>(() => "shown");
                            _ = lazy.Value;
                            object boxed = lazy;
                            return Hash(boxed.ToString()!) + Hash($"{lazy}");
                        }
                        case 15:
                        {
                            // A failed parameterless construction is not cached: each access throws anew.
                            var lazy = new Lazy<string>();
                            Exception? first = null, second = null;
                            try { _ = lazy.Value; } catch (Exception failure) { first = failure; }
                            try { _ = lazy.Value; } catch (Exception failure) { second = failure; }
                            return ReferenceEquals(first, second) || lazy.IsValueCreated ? -2 : Failure(first!) ^ Failure(second!);
                        }
                        case 16: return new Lazy<int?>(LazyThreadSafetyMode.None).Value is null ? 116 : -3;
                        case 17: return new Lazy<Public>().Value.Value + 100;
                        case 18:
                        {
                            string? target = null;
                            try { LazyInitializer.EnsureInitialized(ref target); return -4; }
                            catch (Exception failure) { return Failure(failure); }
                        }
                        case 19:
                        {
                            var table = new ConditionalWeakTable<object, string>();
                            try { table.GetOrCreateValue(new object()); return -5; }
                            catch (Exception failure) { return Failure(failure); }
                        }
                        default: return new ConditionalWeakTable<object, Public>().GetOrCreateValue(new object()).Value + 100;
                    }
                }
                catch (Exception failure) { return -1000 - Failure(failure) % 1000; }
            }
        }
        """, [.. Enumerable.Range(0, 21)]));
}
