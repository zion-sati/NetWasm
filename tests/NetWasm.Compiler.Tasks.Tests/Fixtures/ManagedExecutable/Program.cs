public static class Container
{
    public static class Program
    {
        public static int Main() => 17;
    }
}

namespace NetWasm.Compiler.Tasks.Tests.Fixtures.ManagedExecutable
{
    public static class AlternateProgram
    {
        public static int Main() => 23;
    }

    public static class NoArgumentsVoidProgram
    {
        public static void Main()
        {
        }
    }

    public static class StringArgumentsVoidProgram
    {
        public static void Main(string[] arguments) => _ = arguments;
    }

    public static class StringArgumentsExitCodeProgram
    {
        public static int Main(string[] arguments) => arguments.Length;
    }

    public static class UnsupportedEntryPointProgram
    {
        public static int Invalid(string first, string second) => first.Length + second.Length;
    }

    public static class UnsupportedReturnProgram
    {
        public static string Invalid() => string.Empty;
    }

    public static class UnsupportedSingleParameterProgram
    {
        public static int Invalid(int value) => value;
    }

    public static class UnsupportedArrayParameterProgram
    {
        public static int Invalid(int[] values) => values.Length;
    }

    public static class GenericEntryPointProgram
    {
        public static int Invalid<T>() => 0;
    }

    public sealed class InstanceEntryPointProgram
    {
        private readonly int _value;

        public InstanceEntryPointProgram(int value) => _value = value;

        public int Invalid() => _value;
    }

    public sealed class Marker;
}
