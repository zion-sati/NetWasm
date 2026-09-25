using System.Collections.Immutable;
using System.Text;

namespace NetWasm.Compiler.Tests.Correctness;

internal static class RandomCilFixtureFactory
{
    internal static readonly ImmutableArray<int> PilotSeeds = [46, 17, 43, 95, 406];

    public static CorpusFixture Create(int seed)
    {
        var name = "RandomCil" + seed.ToString(
            System.Globalization.CultureInfo.InvariantCulture)
            .Replace('-', 'N');
        return new(
            name,
            "NetWasm.Correctness.RandomCil",
            CreateSource(),
            ImmutableArray.Create(int.MinValue, -17, -1, 0, 1, 19, int.MaxValue))
        {
            OracleMode = OracleMode.SameIl,
            SameSourceReason = null,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            AllowUnsafe = true,
        };
    }

    public static CorpusFixture CreateExceptionHandling(int seed)
    {
        var source = new StringBuilder(
            "namespace NetWasm.Correctness.RandomCil;\n" +
            "public static class EntryPoint\n{\n" +
            "    private static bool Accept(int value) => value > 0;\n" +
            "    public static int Run(int input)\n    {\n" +
            "        try\n        {\n");
        for (var index = 0; index < 32; index++)
        {
            source.Append("            input = unchecked((input * 3) + ")
                .Append(index)
                .Append(");\n");
        }
        source.Append(
            "            throw new System.Exception();\n" +
            "        }\n" +
            "        catch when (Accept(input))\n        {\n" +
            "            return input + 2;\n" +
            "        }\n" +
            "        finally\n        {\n" +
            "            input += 3;\n" +
            "        }\n" +
            "    }\n" +
            "    public static int Trace() => 0;\n" +
            "}\n");
        return new(
            $"RandomCilEh{seed}",
            "NetWasm.Correctness.RandomCil",
            source.ToString(),
            [-1, 1, 5, 9])
        {
            OracleMode = OracleMode.SameIl,
            SameSourceReason = null,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        };
    }

    internal static string CreateSource()
    {
        var source = new StringBuilder(
            "namespace NetWasm.Correctness.RandomCil;\n" +
            "public interface IBox\n{\n" +
            "    int Add(int input);\n" +
            "}\n" +
            "public interface IStaticTransform\n{\n" +
            "    static abstract int Apply(int input);\n" +
            "}\n" +
            "public readonly struct StaticTransform : IStaticTransform\n{\n" +
            "    public static int Apply(int input) => unchecked(input + 1);\n" +
            "}\n" +
            "public interface IDefaultBox\n{\n" +
            "    int Add(int input) => unchecked(input + 1);\n" +
            "}\n" +
            "public sealed class DefaultBox : IDefaultBox { }\n" +
            "public class BaseBox\n{\n" +
            "    public virtual BaseBox Copy() => new BaseBox();\n" +
            "    public virtual int Read() => 0;\n" +
            "}\n" +
            "public sealed class DerivedBox : BaseBox\n{\n" +
            "    public override DerivedBox Copy() => new DerivedBox();\n" +
            "    public override int Read() => 1;\n" +
            "}\n" +
            "public class Box : IBox\n{\n" +
            "    public int Value;\n" +
            "    public virtual int Add(int input) => unchecked(Value + input);\n" +
            "}\n" +
            "public static class EntryPoint\n{\n" +
            "    private static readonly int[] SeedData = new int[] { 1, 2, 3, 4, 5 };\n" +
            "    public static int StaticValue;\n" +
            "    public static System.Exception? ExceptionValue;\n" +
            "    public static int Helper(int left, int right) => unchecked(left + right);\n" +
            "    public static int HelperUnary(int value) => unchecked(value * 3);\n" +
            "    public static int RangeSlice(int value)\n    {\n" +
            "        var values = new[] { 7, value, 1, 9 };\n" +
            "        var middle = values[1..^1];\n" +
            "        System.Span<int> span = values;\n" +
            "        var tail = span[1..];\n" +
            "        var text = \"abcd\"[1..^1];\n" +
            "        return unchecked(middle[0] + middle[^1] + tail[^1] + text.Length);\n    }\n" +
            "    public static void RetainLocal(ref int value) { }\n" +
            "    public static void RetainBox(ref Box value) { }\n" +
            "    public static unsafe int CallPointer(int value)\n    {\n" +
            "        delegate* managed<int, int> pointer = &HelperUnary;\n" +
            "        return pointer(value);\n    }\n" +
            "    public static System.Type Int32Type() => typeof(int);\n" +
            "    public static string StringValue() => \"random-cil\";\n" +
            "    public static int SeedValue() => SeedData[0];\n" +
            "    public static int ConstrainedHash<T>(T value) where T : struct => " +
            "value.GetHashCode();\n" +
            "    public static int Run(int input)\n    {\n" +
            "        var value = input;\n" +
            "        var box = new Box();\n" +
            "        RetainLocal(ref value);\n" +
            "        RetainBox(ref box);\n");
        for (var index = 0; index < 512; index++)
        {
            source.Append("        value = unchecked((value * 3) + input + ")
                .Append(index)
                .Append(");\n");
        }
        source.Append(
            "        return value;\n" +
            "    }\n" +
            "    public static int Trace() => 0;\n" +
            "}\n");
        return source.ToString();
    }
}
