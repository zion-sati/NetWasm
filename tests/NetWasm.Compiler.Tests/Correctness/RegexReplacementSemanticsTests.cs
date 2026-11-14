namespace NetWasm.Compiler.Tests.Correctness;

// System.Text.RegularExpressions compiles its own helpers (StructListBuilder, ValueListBuilder,
// HashtableExtensions, StackHelper and the Common builders) rather than CoreLib's, and builds a simple
// replacement's result as upstream does, through string.Create over the segments' offsets. Each input
// takes one replacement or parsing path and is checked against desktop .NET.
public sealed class RegexReplacementSemanticsTests(CorrectnessTestRunner runner) : CSharpSemanticTestBase(runner)
{
    [Fact]
    public void ReplacementsSplitsAndGroupsMatchDotNet() => Run(new CorpusFixture(
        "RegexReplacementSemantics", "NetWasm.Correctness.RegexReplacementSemantics",
        """
        using System;
        using System.Text.RegularExpressions;
        namespace NetWasm.Correctness.RegexReplacementSemantics;
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
            public static int Run(int input)
            {
                try
                {
                    return input switch
                    {
                        // A simple replacement left to right: the segments path.
                        0 => Hash(Regex.Replace("a1b22c333d", "[0-9]+", "#")),
                        1 => Hash(Regex.Replace("one two three", "o", "")),
                        2 => Hash(new Regex("ab").Replace("abababab", "x", 2)),
                        3 => Hash(new Regex("ab").Replace("abababab", "x", 3, 1)),
                        4 => Hash(Regex.Replace("no match here", "z+", "#")),
                        // Substitutions and right to left: the other paths.
                        5 => Hash(Regex.Replace("John Smith", @"(?<first>\w+) (?<last>\w+)", "${last}, ${first}")),
                        6 => Hash(new Regex("[0-9]", RegexOptions.RightToLeft).Replace("a1b2c3", "#", 2)),
                        7 => Hash(Regex.Replace("aaa", "a", m => m.Index.ToString())),
                        // Splits, groups and nested patterns.
                        8 => Hash(string.Join("|", Regex.Split("a,b;;c", "[,;]"))),
                        9 => Hash(string.Join("|", Regex.Split("a1b2c", "([0-9])"))),
                        10 => Regex.Match("key=value; other=thing", @"(\w+)=(\w+)").Groups[2].Value == "value" ? 110 : -1,
                        11 => Hash(Regex.Replace("((a)(b(c)))", @"\(([^()]*)\)", "[$1]")),
                        _ => Regex.Matches("x1y22z333", "[0-9]+").Count + 100,
                    };
                }
                catch (Exception failure) { return -1000 - Hash(failure.GetType().FullName + failure.Message) % 1000; }
            }
        }
        """, [.. Enumerable.Range(0, 13)])
    {
        // Regex's match timeout reads the monotonic clock, which a component's world provides.
        RequiresReactor = true,
    });
}
