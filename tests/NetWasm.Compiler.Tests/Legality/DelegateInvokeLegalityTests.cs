using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tests.Legality;

// A delegate's invoke function dispatches a single-cast delegate once and goes round
// the invocation list only for a multicast one: each method gives desktop .NET 11's
// result for one shape it must keep right: a static target, an instance target and a
// capturing closure, two entries in combination order, a list removed down to one
// entry and to none, an entry that throws partway (the earlier entries ran, the later
// did not), a list's result being its last entry's, a ref argument passed through
// every entry, twelve targets of one type (more than the function tests in turn), and
// a sub-list removed from a longer list beside a duplicated entry.
public sealed class DelegateInvokeLegalityTests
{
    private const string Source = """
        using System;

        public sealed class Recorder
        {
            public long Trace = 17;
            public int Count;
            public void Note(int value) { Trace = Trace * 31 + value; Count++; }
        }

        public sealed class Holder
        {
            private readonly Recorder _recorder;
            private readonly int _id;
            public Holder(Recorder recorder, int id) { _recorder = recorder; _id = id; }
            public void Touch(int input) => _recorder.Note(_id * 100 + input);
            public int Value(int input) => _id * 1000 + input;
        }

        public delegate void RefStep(ref int value);

        public static class EntryPoint
        {
            private static Recorder s_recorder = new();

            private static void StaticNote(int input) => s_recorder.Note(input + 7);
            private static void Step1(ref int value) => value = value * 3 + 1;
            private static void Step2(ref int value) => value = value * 5 + 2;
            private static void Step3(ref int value) => value -= 9;
            private static void Thrower(int input)
            {
                s_recorder.Note(input * 2);
                if (input >= 0) throw new InvalidOperationException("stop");
            }

            // One static target: the single-cast path.
            private static int SingleStatic(int input)
            {
                s_recorder = new Recorder();
                Action<int> action = StaticNote;
                action(input);
                return (int)s_recorder.Trace;
            }

            // One instance target and one capturing closure.
            private static int SingleInstanceAndClosure(int input)
            {
                s_recorder = new Recorder();
                var holder = new Holder(s_recorder, 3);
                Action<int> instance = holder.Touch;
                instance(input);
                var captured = input * 11;
                Action<int> closure = value => s_recorder.Note(captured + value);
                closure(input);
                return (int)s_recorder.Trace;
            }

            // Two entries: both run, in combination order.
            private static int TwoEntries(int input)
            {
                s_recorder = new Recorder();
                var first = new Holder(s_recorder, 1);
                var second = new Holder(s_recorder, 2);
                Action<int> both = first.Touch;
                both += second.Touch;
                both(input);
                return (int)(s_recorder.Trace + s_recorder.Count * 1000);
            }

            // A list removed down to one entry, then to none.
            private static int RemovedToOneAndNone(int input)
            {
                s_recorder = new Recorder();
                var first = new Holder(s_recorder, 4);
                var second = new Holder(s_recorder, 5);
                Action<int> first1 = first.Touch;
                Action<int> second1 = second.Touch;
                var both = first1 + second1;
                var one = both - first1;
                one!(input);
                var none = one - second1;
                none?.Invoke(input);
                return (int)(s_recorder.Trace + s_recorder.Count * 1000 + (none is null ? 1 : 0));
            }

            // An entry that throws: the entries before it ran, the ones after did not.
            private static int ThrowsMidway(int input)
            {
                s_recorder = new Recorder();
                var before = new Holder(s_recorder, 6);
                var after = new Holder(s_recorder, 7);
                Action<int> list = before.Touch;
                list += Thrower;
                list += after.Touch;
                var caught = 0;
                try { list(input); }
                catch (InvalidOperationException) { caught = 1; }
                return (int)(s_recorder.Trace + s_recorder.Count * 1000 + caught * 100000);
            }

            // A list's result is its last entry's.
            private static int LastResult(int input)
            {
                Func<int, int> list = new Holder(s_recorder, 1).Value;
                list += new Holder(s_recorder, 2).Value;
                list += new Holder(s_recorder, 3).Value;
                return list(input);
            }

            // A ref argument passes through every entry in turn.
            private static int RefThroughEntries(int input)
            {
                RefStep list = Step1;
                list += Step2;
                list += Step3;
                var value = input;
                list(ref value);
                return value;
            }

            // Twelve targets of one delegate type: more than the invoke function tests in turn.
            private static int ManyTargets(int input)
            {
                s_recorder = new Recorder();
                Action<int>? list = null;
                for (var id = 0; id < 12; id++)
                {
                    var holder = new Holder(s_recorder, id + 10);
                    list += id % 3 == 0 ? holder.Touch : id % 3 == 1 ? (Action<int>)StaticNote : value => s_recorder.Note(value - id);
                }
                list!(input);
                return (int)(s_recorder.Trace + s_recorder.Count * 1000);
            }

            // A sub-list removed from the end of a longer list, and a duplicated entry.
            private static int SubListAndDuplicate(int input)
            {
                s_recorder = new Recorder();
                Action<int> a = new Holder(s_recorder, 21).Touch;
                Action<int> b = new Holder(s_recorder, 22).Touch;
                Action<int> c = new Holder(s_recorder, 23).Touch;
                var all = a + b + c;
                var rest = all - (b + c);
                rest!(input);
                var twice = a + a;
                twice(input);
                var once = twice - a;
                once!(input);
                return (int)(s_recorder.Trace + s_recorder.Count * 1000);
            }

            public static int Run(int input)
            {
                long hash = 17;
                foreach (var value in new[] { SingleStatic(input), SingleInstanceAndClosure(input), TwoEntries(input),
                             RemovedToOneAndNone(input), ThrowsMidway(input), LastResult(input), RefThroughEntries(input),
                             ManyTargets(input), SubListAndDuplicate(input) })
                {
                    hash = hash * 31 + value;
                }
                return (int)(hash ^ (hash >> 32));
            }
        }

        """;

    // As desktop .NET 11 computes them.
    private static readonly (int Input, int Expected)[] Cases =
        [(0, -857064059), (1, 646095035), (3, -642541187), (7, 1074920842), (-4, -944012246)];

    // A multicast function dispatches its entries itself at O1 to O3 and hands them to the
    // invoke function at the other presets; each preset is also run with the choice forced.
    [Theory]
    [InlineData(FinalWasmOptimization.O0, ManagedMulticastInlineDispatch.Auto)]
    [InlineData(FinalWasmOptimization.O3, ManagedMulticastInlineDispatch.Auto)]
    [InlineData(FinalWasmOptimization.O3, ManagedMulticastInlineDispatch.Disabled)]
    [InlineData(FinalWasmOptimization.Oz, ManagedMulticastInlineDispatch.Auto)]
    [InlineData(FinalWasmOptimization.Oz, ManagedMulticastInlineDispatch.Enabled)]
    public void InvokesEveryDelegateShapeAsDesktopDotNetDoes(
        FinalWasmOptimization optimization, ManagedMulticastInlineDispatch dispatch) =>
        LegalityCases.Run($"DelegateInvokeLegality{optimization}{dispatch}", Source, Cases, optimization,
            new WasmOptimizationOverrides(MulticastInlineDispatch: dispatch));
}
