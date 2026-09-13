namespace System
{
    public static partial class GC
    {
        public static void SuppressFinalize(object value)
        {
        }

        public static void ReRegisterForFinalize(object value)
        {
        }
    }

    public static class Activator
    {
        public static T CreateInstance<T>()
        {
            return default(T)!;
        }
    }

    public static class Console
    {
        public static IO.TextWriter Out => StandardOutput.Writer;

        public static IO.TextWriter Error => StandardError.Writer;

        public static void WriteLine(int value) => Out.WriteLine(value);

        public static void WriteLine(string? value) => Out.WriteLine(value);

        private static class StandardOutput
        {
            internal static readonly IO.TextWriter Writer =
                Runtime.InteropServices.ConsoleServices.ConsoleCompositionRoot
                    .CreateStandardOutputWriter();
        }

        private static class StandardError
        {
            internal static readonly IO.TextWriter Writer =
                Runtime.InteropServices.ConsoleServices.ConsoleCompositionRoot
                    .CreateStandardErrorWriter();
        }
    }
}
