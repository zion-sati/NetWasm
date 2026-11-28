// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text;

namespace NetWasm.Compiler.Tests.Correctness;

// Runtime-async methods built from a seed, compared with desktop .NET: the shapes the lowering and the structurer have
// got wrong, combined at random. Protected blocks nested at one start, awaits in try, catch and finally bodies, filters,
// loops with break and continue, early returns and throws, argument checks before a loop, switches, await using, and
// values the lowering spills across an await (fields, statics, conversions, values where paths meet, strings, array
// elements, struct fields). Each await completes at once, suspends, or suspends and then completes, by the input and the
// site, so every path runs: a continuation is queued and Run pumps the queue until the entry method completes, the same
// on desktop and on NetWasm, with no thread pool and no event loop.
internal static class RuntimeAsyncShapeGenerator
{
    internal static readonly int[] Inputs = [0, 1, 2, 3, 4, 5, 7, 11];

    public static string Source(int seed, string namespaceName)
    {
        var random = new Xorshift(seed);
        var methods = new StringBuilder();
        for (var method = 2; method >= 0; method--)
        {
            methods.Append(new MethodWriter(random, method).Write());
        }
        return $$"""
            using System;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;

            namespace {{namespaceName}};

            public readonly struct Later<T>(T value) : INotifyCompletion
            {
                public Later<T> GetAwaiter() => this;
                public bool IsCompleted => false;
                public T GetResult() => value;
                public void OnCompleted(Action continuation) => EntryPoint.Pending.Enqueue(continuation);
            }

            public sealed class Box
            {
                public int Value;
            }

            public class Shape
            {
                public virtual int Weight => 1;
            }

            public sealed class Square : Shape
            {
                public override int Weight => 4;
            }

            public sealed class Circle : Shape
            {
                public override int Weight => 7;
            }

            public struct Pair
            {
                public int Left;
                public long Right;
            }

            public sealed class Fault(int code) : Exception
            {
                public int Code { get; } = code;
            }

            public sealed class Resource(int id) : IAsyncDisposable
            {
                public async ValueTask DisposeAsync()
                {
                    EntryPoint.Mark(500 + id);
                    if ((id & 1) == 0) await new Later<int>(id);
                }
            }

            public static class EntryPoint
            {
                internal static readonly Queue<Action> Pending = new();
                private static readonly Box Shared = new();
                private static int _trace;

                public static int Run(int input)
                {
                    _trace = 17;
                    Shared.Value = 0;
                    Pending.Clear();
                    var task = M0(input);
                    for (var step = 0; !task.IsCompleted && Pending.Count != 0 && step < 100000; step++)
                    {
                        Pending.Dequeue()();
                    }
                    if (!task.IsCompleted) return -1;
                    return unchecked(_trace * 31 + (task.IsFaulted ? Code(task.Exception!.InnerException) : task.Result));
                }

                public static int Trace() => _trace;

                internal static void Mark(int value) => _trace = unchecked(_trace * 31 + value);

                private static int Code(Exception? exception) => exception switch
                {
                    Fault fault => 1000 + fault.Code,
                    InvalidOperationException => 2,
                    ArgumentOutOfRangeException => 3,
                    _ => 9,
                };

                // Completes at once, suspends, or suspends and then completes, by the input and the site.
                private static async ValueTask<int> Step(int input, int site)
                {
                    switch ((input + site) % 3)
                    {
                        case 0:
                            return site;
                        case 1:
                            return await new Later<int>(site + 1);
                        default:
                            await new Later<int>(0);
                            return site + 2;
                    }
                }

                private static async Task<int> TaskStep(int input, int site) => await Step(input, site) + 1;

                private static async ValueTask<string> Name(int input, int site) =>
                    ((input + site) & 1) == 0 ? "n" + site : await new Later<string>("m" + site);

                private static int Pick(Box? box, int value) => (box?.Value ?? -1) + value;

                private static int Weigh(Shape shape, int value) => shape.Weight * 3 + value;

            {{methods}}}
            """;
    }

    private sealed class Xorshift(int seed)
    {
        private uint _state = unchecked((uint)seed * 2654435761u) | 1;

        public int Next(int bound)
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (int)(_state % (uint)bound);
        }

        public bool Chance(int percent) => Next(100) < percent;
    }

    // One method, M{index}: locals, argument checks, then statements; it calls the next method, if any, at an await.
    private sealed class MethodWriter(Xorshift random, int index)
    {
        private readonly StringBuilder _text = new();
        private int _sites = index * 100;
        private int _loops;
        private int _budget = 26;

        public string Write()
        {
            Line(1, $"private static async Task<int> M{index}(int input)");
            Line(1, "{");
            Line(2, "if (input < -1000) throw new ArgumentOutOfRangeException(nameof(input));");
            Line(2, "var x = input;");
            Line(2, "long l = 0;");
            Line(2, "var s = \"\";");
            Line(2, "var box = new Box();");
            Line(2, "var array = new int[4];");
            Line(2, "var pair = new Pair();");
            Statements(2, depth: 0, inLoop: false, inFinally: false, count: 4 + random.Next(4));
            if (index < 2)
            {
                Line(2, $"x += await M{index + 1}(x & 15);");
            }
            Line(2, "return unchecked(x + (int)l + s.Length + box.Value + array[1] + pair.Left + (int)pair.Right);");
            Line(1, "}");
            Line(0, "");
            return _text.ToString();
        }

        private void Statements(int indent, int depth, bool inLoop, bool inFinally, int count)
        {
            for (var statement = 0; statement < count && _budget > 0; statement++)
            {
                _budget--;
                Statement(indent, depth, inLoop, inFinally);
            }
        }

        private void Statement(int indent, int depth, bool inLoop, bool inFinally)
        {
            var nested = depth < 3 && _budget > 2;
            var choice = random.Next(nested ? 22 : 12);
            switch (choice)
            {
                case 0:
                    Line(indent, $"Mark({Site()});");
                    break;
                case 1:
                case 2:
                    Line(indent, $"x += await Step(input, {Site()});");
                    break;
                case 3:
                    Line(indent, $"x += await TaskStep(x, {Site()});");
                    break;
                case 4:
                case 5:
                    Spill(indent);
                    break;
                case 6:
                    Line(indent, $"if ({Condition()}) {Exit(inLoop, inFinally)}");
                    break;
                case 7:
                    Line(indent, $"if ({Condition()}) throw new Fault({Site() % 7});");
                    break;
                case 8:
                    Line(indent, $"x = unchecked(x * 3 + {Site()});");
                    break;
                case 9:
                    Line(indent, $"x += await Task.FromResult({Site()});");
                    break;
                case 10:
                    Line(indent, $"if ({Condition()}) x += await Step(x, {Site()}); else Mark({Site()});");
                    break;
                case 11:
                    Line(indent, $"x += {Site()} + await Step(input, {Site()});");
                    break;
                case 12:
                case 13:
                    If(indent, depth, inLoop, inFinally);
                    break;
                case 14:
                case 15:
                    Loop(indent, depth, inFinally);
                    break;
                case 16:
                case 17:
                    Try(indent, depth, inLoop, inFinally);
                    break;
                case 18:
                    NestedTry(indent, depth, inLoop, inFinally);
                    break;
                case 19:
                    Switch(indent, depth, inLoop, inFinally);
                    break;
                case 20:
                    Line(indent, $"await using (var resource{Site()} = new Resource({Site()}))");
                    Block(indent, depth, inLoop, inFinally);
                    break;
                default:
                    Line(indent, $"if ({Condition()}) return x + {Site()};".Replace("return", inFinally ? "x +=" : "return"));
                    break;
            }
        }

        // A value Roslyn may leave on the evaluation stack under the awaited one.
        private void Spill(int indent)
        {
            var site = Site();
            Line(indent, random.Next(10) switch
            {
                0 => $"box.Value = await Step(input, {site});",
                1 => $"Shared.Value += await Step(x, {site});",
                2 => $"x = (byte)(x * 7) + await Step(input, {site});",
                3 => $"l = (long)x * 3 + await Step(input, {site}); x += (int)(l % 1000);",
                4 => $"x += Pick((x & {1 + site % 3}) == 0 ? null : box, await Step(input, {site}));",
                5 => $"x += Weigh((x & 1) == 0 ? new Square() : new Circle(), await Step(input, {site}));",
                6 => $"s = s + await Name(x, {site}); x += s.Length;",
                7 => $"array[{site % 4}] += await Step(x, {site});",
                8 => $"pair.Left = await Step(input, {site}); pair.Right += pair.Left;",
                _ => $"x += ((x & 2) == 0 ? (byte)x : {site % 9}) + await Step(input, {site});",
            });
        }

        private void If(int indent, int depth, bool inLoop, bool inFinally)
        {
            Line(indent, $"if ({Condition()})");
            Block(indent, depth, inLoop, inFinally);
            if (random.Chance(50))
            {
                Line(indent, "else");
                Block(indent, depth, inLoop, inFinally);
            }
        }

        private void Loop(int indent, int depth, bool inFinally)
        {
            var counter = $"i{index}_{_loops++}";
            if (random.Chance(50))
            {
                Line(indent, $"for (var {counter} = 0; {counter} < {2 + random.Next(3)}; {counter}++)");
            }
            else
            {
                Line(indent, $"var {counter} = 0;");
                Line(indent, $"while ({counter}++ < {2 + random.Next(3)})");
            }
            Block(indent, depth, inLoop: true, inFinally);
        }

        private void Try(int indent, int depth, bool inLoop, bool inFinally)
        {
            Line(indent, "try");
            Block(indent, depth, inLoop, inFinally);
            var handlers = random.Next(3);
            if (handlers != 1)
            {
                Catch(indent, depth, inLoop, inFinally);
            }
            if (handlers != 0)
            {
                Line(indent, "finally");
                Block(indent, depth, inLoop: false, inFinally: true);
            }
        }

        // C#'s try whose first statement is another try: Roslyn starts both at one offset.
        private void NestedTry(int indent, int depth, bool inLoop, bool inFinally)
        {
            Line(indent, "try");
            Line(indent, "{");
            Line(indent + 1, "try");
            Block(indent + 1, depth + 1, inLoop, inFinally);
            if (random.Chance(50))
            {
                Catch(indent + 1, depth + 1, inLoop, inFinally);
            }
            else
            {
                Line(indent + 1, "finally");
                Block(indent + 1, depth + 1, inLoop: false, inFinally: true);
            }
            Statements(indent + 1, depth + 1, inLoop, inFinally, 1 + random.Next(2));
            Line(indent, "}");
            if (random.Chance(50))
            {
                Catch(indent, depth, inLoop, inFinally);
            }
            else
            {
                Line(indent, "finally");
                Block(indent, depth, inLoop: false, inFinally: true);
            }
        }

        private void Catch(int indent, int depth, bool inLoop, bool inFinally)
        {
            var fault = $"fault{Site()}";
            Line(indent, random.Next(3) switch
            {
                0 => $"catch (Fault {fault}) when ({fault}.Code % 2 == {random.Next(2)})",
                1 => $"catch (Fault {fault}) when ({fault}.Code >= 0)",
                _ => "catch (InvalidOperationException)",
            });
            Line(indent, "{");
            Line(indent + 1, $"Mark({Site()});");
            Statements(indent + 1, depth + 1, inLoop, inFinally, random.Next(3));
            Line(indent, "}");
        }

        private void Switch(int indent, int depth, bool inLoop, bool inFinally)
        {
            Line(indent, $"switch ((x + {Site()}) % 4)");
            Line(indent, "{");
            for (var label = 0; label < 3; label++)
            {
                Line(indent + 1, label == 2 ? "default:" : $"case {label}:");
                Line(indent + 1, "{");
                Statements(indent + 2, depth + 1, inLoop, inFinally, 1 + random.Next(2));
                Line(indent + 2, "break;");
                Line(indent + 1, "}");
            }
            Line(indent, "}");
        }

        private void Block(int indent, int depth, bool inLoop, bool inFinally)
        {
            Line(indent, "{");
            Statements(indent + 1, depth + 1, inLoop, inFinally, 1 + random.Next(3));
            Line(indent, "}");
        }

        // Leaves the innermost loop or the method; a finally body may do neither, so it only adds.
        private string Exit(bool inLoop, bool inFinally) => inFinally
            ? $"x += {Site()};"
            : inLoop && random.Chance(60)
                ? random.Chance(50) ? "break;" : "continue;"
                : random.Chance(30)
                    ? "throw new InvalidOperationException();"
                    : $"return x + {Site()};";

        private string Condition() => random.Next(4) switch
        {
            0 => $"(input + {Site()}) % {2 + random.Next(4)} == 0",
            1 => $"(x & {1 << random.Next(4)}) != 0",
            2 => $"x % {3 + random.Next(4)} == {random.Next(3)}",
            _ => $"box.Value > {random.Next(6)}",
        };

        private int Site() => ++_sites;

        private void Line(int indent, string text) => _text.Append(' ', indent * 4).Append(text).Append('\n');
    }
}
