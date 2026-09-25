using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal static class RandomExceptionHandlingFixtureFactory
{
    internal static readonly ImmutableArray<int> PilotSeeds = [101, 509];

    public static CorpusFixture Create(int seed)
    {
        var namespaceName = $"NetWasm.Correctness.RandomEh.Seed{seed}";
        var finallyValue = Math.Abs(seed % 17) + 3;
        var filterValue = Math.Abs(seed % 11) + 5;
        var outerValue = Math.Abs(seed % 23) + 7;
        return new(
            $"RandomExceptionHandling{seed}",
            namespaceName,
            $$"""
            namespace {{namespaceName}};

            public static class EntryPoint
            {
                private static int _trace;
                private static Node? _root;

                public static int Run(int input)
                {
                    _trace = {{seed}};
                    _root = new Node { Value = input };
                    try
                    {
                        try
                        {
                            _trace += Allocate(input);
                            if ((input & 1) == 0)
                            {
                                throw new System.ArgumentException();
                            }
                            return _trace + _root.Value;
                        }
                        catch (System.ArgumentException) when (Filter(input))
                        {
                            _trace += _root!.Value;
                            if ((input & 4) != 0)
                            {
                                throw;
                            }
                            return _trace;
                        }
                        finally
                        {
                            _trace += {{finallyValue}};
                        }
                    }
                    catch (System.ArgumentException)
                    {
                        _trace += {{outerValue}};
                        return _trace;
                    }
                }

                public static int Trace() => _trace;

                private static bool Filter(int input)
                {
                    _trace += Allocate({{filterValue}});
                    return (input & 2) == 0;
                }

                private static int Allocate(int value)
                {
                    var temporary = new Node { Value = value + 1 };
                    return temporary.Value;
                }
            }

            public sealed class Node
            {
                public int Value;
            }
            """,
            [-6, -4, -2, 0, 1, 2, 4, 6, 9])
        {
            OracleMode = OracleMode.SameIl,
            SameSourceReason = null,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        };
    }
}
