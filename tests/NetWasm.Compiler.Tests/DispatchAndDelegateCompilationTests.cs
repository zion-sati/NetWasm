using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static NetWasm.Compiler.Tests.CompilerTestSupport;

public sealed class DispatchAndDelegateCompilationTests
{
    [Fact]
    public void CompilerExecutesObjectToInterfaceCastAfterArrayLookup()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "InterfaceCastCompilerFixture",
            """
            namespace InterfaceCastCompilerFixture;

            interface IItem
            {
                int Value { get; }
            }

            sealed class Item : IItem
            {
                public int Value => 42;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    object[] values = [new Item()];
                    var items = new IItem[values.Length];
                    items[0] = (IItem)values[0];
                    return items[0].Value;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "InterfaceCastCompilerFixture.EntryPoint",
            "Run",
            []));

        Assert.Contains(result.Program.TypeTestSites.Values, site =>
            site.TargetType.FullName == "InterfaceCastCompilerFixture.IItem" &&
            site.MatchingTypes.Any(type =>
                type.FullName == "InterfaceCastCompilerFixture.Item"));
        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 0));
    }

    [Fact]
    public void CompilerRejectsIncompatibleValueStoredThroughCovariantArrayReference()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "InterfaceArrayMismatchCompilerFixture",
            """
            namespace InterfaceArrayMismatchCompilerFixture;

            interface IItem
            {
            }

            sealed class Other
            {
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    object[] items = new IItem[1];
                    try
                    {
                        items[0] = new Other();
                        return -1;
                    }
                    catch (System.ArrayTypeMismatchException)
                    {
                        return 42;
                    }
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "InterfaceArrayMismatchCompilerFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 0));
    }

    [Fact]
    public void CompilerExecutesAndTrimsVirtualAndInterfaceDispatch()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DispatchCompilerFixture",
            """
            namespace DispatchCompilerFixture;

            public interface IRead
            {
                int Read(int value);
            }

            public interface IExplicitRead
            {
                int Read(int value);
            }

            public abstract class Base
            {
                public abstract int Read(int value);
            }

            public sealed class Marker
            {
            }

            public sealed class Derived : Base, IRead, IExplicitRead
            {
                public override int Read(int value)
                {
                    var marker = new Marker();
                    return KeepAlive(marker, value + 1);
                }

                private static int KeepAlive(Marker marker, int value) => value;
                int IExplicitRead.Read(int value) => value + 2;
            }

            public sealed class Unreachable : Base
            {
                public override int Read(int value) => value + 1_000;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var concrete = new Derived();
                    Base throughBase = concrete;
                    IRead throughInterface = concrete;
                    IExplicitRead throughExplicitInterface = concrete;
                    object candidate = concrete;
                    IRead throughCast = (IRead)candidate;
                    object unrelated = new Marker();
                    object missing = null;
                    return throughBase.Read(input)
                        + throughInterface.Read(input)
                        + throughExplicitInterface.Read(input)
                        + throughCast.Read(input)
                        + (candidate is IRead ? 1 : 0)
                        + (unrelated is IRead ? 1_000 : 0)
                        + (missing is IRead ? 10_000 : 0);
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DispatchCompilerFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(4, result.Program.DispatchCallSites.Count);
        Assert.All(result.Program.DispatchCallSites.Values, callSite =>
        {
            var target = Assert.Single(callSite.Targets);
            Assert.Equal("DispatchCompilerFixture.Derived", target.ReceiverType.FullName);
        });
        Assert.DoesNotContain(result.Program.MethodInstances.Values, method =>
            method.Definition.Name == "Read" &&
            method.DeclaringType.FullName == "DispatchCompilerFixture.Unreachable");
        Assert.Equal(4, result.Program.TypeTestSites.Count);
        Assert.Contains(result.Program.TypeTestSites.Values, site =>
            site.TargetType.FullName == "DispatchCompilerFixture.IRead" &&
            site.MatchingTypes.Length == 1 &&
            site.MatchingTypes[0].FullName == "DispatchCompilerFixture.Derived");
        var entryRoots = result.Program.RootMaps[result.Program.EntryPoint.Key];
        var allocatingDispatches = result.Program.DispatchCallSites.Values
            .Where(callSite => callSite.Targets.Any(target =>
                result.Program.AllocatingMethods.Contains(target.Method.Definition.Key)))
            .ToArray();
        Assert.Equal(3, allocatingDispatches.Length);
        Assert.All(allocatingDispatches, callSite =>
            Assert.Contains(callSite.IlOffset, entryRoots.Safepoints.Keys));
        Assert.Equal(170, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesStaticClosedAndVirtualDelegates()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DelegateCompilerFixture",
            """
            namespace DelegateCompilerFixture;

            public delegate int Compute(int value);

            public class Target
            {
                private readonly int _offset;

                public Target(int offset)
                {
                    _offset = offset;
                }

                public int Add(int value) => value + _offset;
                public virtual int Virtual(int value) => value + _offset + 1;
            }

            public sealed class Derived : Target
            {
                public Derived(int offset) : base(offset)
                {
                }

                public override int Virtual(int value) => value + 4;
            }

            public static class EntryPoint
            {
                private static int Static(int value) => value + 1;

                public static int Run(int input)
                {
                    Compute first = Static;
                    var target = new Target(2);
                    Compute second = target.Add;
                    Target derived = new Derived(3);
                    Compute third = derived.Virtual;
                    Compute combined = first + second;
                    combined += third;
                    int multicastLast = combined(input);
                    combined -= third;
                    int removedLast = combined(input);
                    return first(input) + second(input) + third(input)
                        + multicastLast + removedLast
                        + (first == first ? 1 : 0)
                        + (first != second ? 1 : 0);
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DelegateCompilerFixture.EntryPoint",
            "Run",
            []));

        Assert.True(result.Program.CallableMethods.Count >= 3);
        Assert.Contains(result.Program.DispatchCallSites.Values, callSite =>
            callSite.Declaration.Definition.Name == "Virtual" &&
            callSite.Targets.Any(target => target.Method.DeclaringType.FullName ==
                "DelegateCompilerFixture.Derived"));
        Assert.Equal(220, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerUsesInvocationSequenceSemanticsForMulticastDelegates()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "MulticastSequenceCompilerFixture",
            """
            namespace MulticastSequenceCompilerFixture;

            public delegate void Handler();
            public delegate void OtherHandler();

            public sealed class Target
            {
                public void Invoke() { }
            }

            public static class EntryPoint
            {
                private static int _trace;
                private static void A() => _trace = _trace * 10 + 1;
                private static void B() => _trace = _trace * 10 + 2;
                private static void C() => _trace = _trace * 10 + 3;
                private static void Other() { }

                public static int Run(int input)
                {
                    int passed = 0;
                    Handler a = A;
                    Handler b = B;
                    Handler c = C;
                    Handler left = (a + b) + c;
                    Handler right = a + (b + c);
                    if (left == right) passed += 1;
                    if (left != c + b + a) passed += 2;
                    if (left.Equals((object)right)) passed += 1024;
                    if (left.GetHashCode() == right.GetHashCode()) passed += 2048;

                    Handler source = a + b + a + b + c;
                    Handler value = a + b;
                    Handler removed = source - value;
                    _trace = 0;
                    removed();
                    if (_trace == 123) passed += 4;
                    if ((left - right) == null) passed += 8;
                    Handler missing = c + c;
                    if (object.ReferenceEquals(source - missing, source)) passed += 16;
                    if (object.ReferenceEquals(Handler.Remove(source, null), source)) passed += 32;
                    if (object.ReferenceEquals(Handler.Combine(null, a), a) &&
                        object.ReferenceEquals(Handler.Combine(a, null), a)) passed += 64;

                    var firstTarget = new Target();
                    var secondTarget = new Target();
                    Handler firstInstance = firstTarget.Invoke;
                    Handler secondInstance = secondTarget.Invoke;
                    if (firstInstance != secondInstance) passed += 128;

                    OtherHandler other = Other;
                    try { _ = System.Delegate.Combine(a, other); }
                    catch (System.ArgumentException) { passed += 256; }
                    try { _ = System.Delegate.Remove(a, other); }
                    catch (System.ArgumentException) { passed += 512; }
                    return passed;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "MulticastSequenceCompilerFixture.EntryPoint",
            "Run",
            []));

        var operations = result.Program.Methods.Values
            .SelectMany(method => method.Body.Instructions)
            .Select(instruction => instruction.Operation)
            .ToArray();
        Assert.Contains(CilOperation.DelegateCombine, operations);
        Assert.Contains(CilOperation.DelegateRemove, operations);
        Assert.Equal(2, result.Program.DelegateTypes.Length);
        Assert.Equal(2, result.Program.DelegateTypes
            .Select(type => result.Layouts.ObjectIdentityLayouts[type].TypeId)
            .Distinct().Count());
        Assert.Equal(4095, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesFieldLikeEventsWithDuplicateSubscriptionAndRemoval()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "EventCompilerFixture",
            """
            namespace EventCompilerFixture;

            public delegate void Handler(int value);

            public sealed class Publisher
            {
                public event Handler? Changed;

                public void Raise(int value) => Changed?.Invoke(value);
            }

            public sealed class Sink
            {
                public int Total;
                public void OnChanged(int value) => Total = Total + value;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var publisher = new Publisher();
                    var sink = new Sink();
                    Handler handler = sink.OnChanged;
                    publisher.Changed += handler;
                    publisher.Changed += handler;
                    publisher.Raise(3);
                    publisher.Changed -= handler;
                    publisher.Raise(5);
                    return input + sink.Total;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EventCompilerFixture.EntryPoint",
            "Run",
            []));

        Assert.Contains(result.Program.Methods.Values
            .SelectMany(method => method.Body.Instructions),
            instruction => instruction.Operation == CilOperation.CompareExchange);
        Assert.Equal(52, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesDelegatesWithReferenceBearingValueArgumentsAndReturns()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ValueDelegateCompilerFixture",
            """
            namespace ValueDelegateCompilerFixture;

            public sealed class Node
            {
            }

            public struct Pair
            {
                public Node Reference;
                public int Number;
            }

            public delegate Pair Transform(Pair value);

            public static class EntryPoint
            {
                private static Pair Increment(Pair value)
                {
                    value.Number = value.Number + 1;
                    return value;
                }

                private static int Consume(Node value, int number) => number;

                public static int Run(int input)
                {
                    Pair value = default;
                    value.Reference = new Node();
                    value.Number = input;
                    Transform transform = Increment;
                    Pair result = transform(value);
                    return Consume(result.Reference, result.Number);
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ValueDelegateCompilerFixture.EntryPoint",
            "Run",
            []));

        Assert.Contains(result.Program.CallableMethods.Values, method =>
            method.Definition.Name == "Increment");
        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }
}
