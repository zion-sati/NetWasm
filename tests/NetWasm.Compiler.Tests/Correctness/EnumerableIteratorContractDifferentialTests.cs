namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class EnumerableIteratorContractDifferentialTests(
    CorrectnessTestRunner runner)
{
    [Fact]
    public void EnumerationStagesCleanupAndIteratorExitsMatchDesktop()
    {
        runner.Run(new(
            "EnumerableIteratorContract",
            "NetWasm.Correctness.EnumerableContract",
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;

            namespace NetWasm.Correctness.EnumerableContract;

            public static class Log
            {
                public static int Value;
                public static void Add(int value) => Value = unchecked(Value * 31 + value);
            }

            public sealed class Sequence : IEnumerable<int>
            {
                private readonly int _mode;
                public Sequence(int mode) => _mode = mode;
                public IEnumerator<int> GetEnumerator()
                {
                    Log.Add(1);
                    if (_mode == 5) throw new InvalidOperationException();
                    return new Enumerator(_mode);
                }
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public sealed class Enumerator : IEnumerator<int>
            {
                private readonly int _mode;
                private int _index = -1;
                public Enumerator(int mode) => _mode = mode;
                public bool MoveNext()
                {
                    Log.Add(2);
                    if (_mode == 6) throw new InvalidOperationException();
                    _index++;
                    return _index < (_mode == 0 ? 0 : _mode == 1 ? 1 : 3);
                }
                public int Current
                {
                    get
                    {
                        Log.Add(3);
                        if (_mode == 7) throw new InvalidOperationException();
                        return _index + 1;
                    }
                }
                object IEnumerator.Current => Current;
                public void Dispose()
                {
                    Log.Add(5);
                    if (_mode == 9) throw new InvalidOperationException();
                }
                public void Reset() => throw new NotSupportedException();
            }

            public struct StructEnumerator
            {
                private int _index;
                public bool MoveNext() { Log.Add(12); return _index++ == 0; }
                public int Current { get { Log.Add(13); return 4; } }
                public void Dispose() => Log.Add(15);
            }

            public sealed class StructSequence
            {
                public StructEnumerator GetEnumerator() { Log.Add(11); return default; }
            }

            public static class EntryPoint
            {
                private static int _trace;
                public static int Trace() => _trace;

                public static int Run(int input)
                {
                    Log.Value = 0;
                    try
                    {
                        if (input == 10) ConsumeStruct();
                        else if (input >= 11) ConsumeIterator(input);
                        else ConsumeSequence(input);
                    }
                    catch (InvalidOperationException) { Log.Add(8); }
                    _trace = Log.Value;
                    return Log.Value;
                }

                private static void ConsumeSequence(int mode)
                {
                    foreach (var value in new Sequence(mode))
                    {
                        Log.Add(4);
                        if (mode == 8) throw new InvalidOperationException();
                        if (mode == 2) break;
                        if (mode == 3 && value == 1) continue;
                        if (mode == 4) return;
                        Log.Add(value + 20);
                    }
                }

                private static void ConsumeStruct()
                {
                    foreach (var value in new StructSequence()) Log.Add(value + 10);
                }

                private static void ConsumeIterator(int mode)
                {
                    foreach (var value in Outer(mode))
                    {
                        Log.Add(value + 40);
                        if (mode == 13) break;
                    }
                }

                private static IEnumerable<int> Outer(int mode)
                {
                    try
                    {
                        foreach (var value in Inner(mode)) yield return value;
                        if (mode == 12) yield break;
                        yield return 3;
                    }
                    finally { Log.Add(35); }
                }

                private static IEnumerable<int> Inner(int mode)
                {
                    try
                    {
                        Log.Add(31);
                        yield return 1;
                        if (mode == 14) throw new InvalidOperationException();
                        yield return 2;
                    }
                    finally { Log.Add(32); }
                }
            }
            """,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14])
        {
            CaptureCompilerDiagnostics = true,
        });
    }
}
