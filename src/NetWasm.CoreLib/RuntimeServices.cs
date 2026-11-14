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

    public static partial class Activator
    {
        public static object? CreateInstance(
            [Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] Type type,
            params object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(type);
            throw new PlatformNotSupportedException("Activator.CreateInstance with a runtime Type and constructor arguments is not available on this NetWasm platform.");
        }

        // As upstream's ActivatorCache and CreateInstanceOfT: a value type, or a concrete class with
        // a public parameterless constructor, is constructed, and the constructor's exception is
        // wrapped in TargetInvocationException; a non-public parameterless constructor and every
        // other T throw what .NET throws. The compiler answers the two questions for the closed T
        // and replaces CreateDefaultInstance<T>() with the construction.
        public static T CreateInstance<T>()
        {
            if (!HasDefaultConstructor<T>())
            {
                if (HasNonPublicDefaultConstructor<T>())
                {
                    throw new MissingMethodException(SR.Format(SR.Arg_NoDefCTor, typeof(T)));
                }
                if (typeof(T) != typeof(string) && !typeof(T).IsInterface && !typeof(T).IsAbstract &&
                    typeof(T).IsSubclassOf(typeof(Delegate)))
                {
                    throw new ArgumentException(
                        SR.Format(SR.Activator_CannotCreateInstance, typeof(T), SR.NotSupported_Type));
                }
                throw new MissingMethodException(SR.Format(
                    SR.Activator_CannotCreateInstance,
                    typeof(T),
                    typeof(T) == typeof(string) ? SR.Argument_NoUninitializedStrings :
                    typeof(T).IsInterface ? SR.Acc_CreateInterface :
                    typeof(T).IsAbstract ? SR.Acc_CreateAbst :
                    typeof(T).HasElementType ? SR.NotSupported_Type :
                    SR.Arg_NoDefCTorWithoutTypeName));
            }
            try
            {
                return CreateDefaultInstance<T>();
            }
            catch (Exception exception)
            {
                throw new Reflection.TargetInvocationException(exception);
            }
        }

        // The compiler specializes these for the closed T.
        private static bool HasDefaultConstructor<T>() =>
            throw new PlatformNotSupportedException("Constructor eligibility requires compiler specialization.");

        private static bool HasNonPublicDefaultConstructor<T>() =>
            throw new PlatformNotSupportedException("Constructor eligibility requires compiler specialization.");

        private static T CreateDefaultInstance<T>() =>
            throw new PlatformNotSupportedException("Construction requires compiler specialization.");
    }

    public static partial class Console
    {
        private static IO.TextReader? s_in;
        private static IO.TextWriter? s_out;
        private static IO.TextWriter? s_error;
        private static bool? s_isInputRedirected;
        private static bool? s_isOutputRedirected;
        private static bool? s_isErrorRedirected;

        public static IO.TextReader In => s_in ?? StandardInput.Reader;

        public static IO.TextWriter Out => s_out ?? StandardOutput.Writer;

        public static IO.TextWriter Error => s_error ?? StandardError.Writer;

        // A redirected stream is one that is not a terminal. Each is asked once, on first use.
        public static bool IsInputRedirected => s_isInputRedirected ??=
            !Runtime.InteropServices.PlatformServices.StandardStreams.IsTerminal(
                Runtime.InteropServices.WebAssembly.StandardStream.Input);

        public static bool IsOutputRedirected => s_isOutputRedirected ??=
            !Runtime.InteropServices.PlatformServices.StandardStreams.IsTerminal(
                Runtime.InteropServices.WebAssembly.StandardStream.Output);

        public static bool IsErrorRedirected => s_isErrorRedirected ??=
            !Runtime.InteropServices.PlatformServices.StandardStreams.IsTerminal(
                Runtime.InteropServices.WebAssembly.StandardStream.Error);

        public static void SetIn(IO.TextReader newIn)
        {
            ArgumentNullException.ThrowIfNull(newIn);
            // TODO(netwasm-threading)
            s_in = newIn;
        }

        public static void SetOut(IO.TextWriter newOut)
        {
            ArgumentNullException.ThrowIfNull(newOut);
            // TODO(netwasm-threading)
            s_out = newOut;
        }

        public static void SetError(IO.TextWriter newError)
        {
            ArgumentNullException.ThrowIfNull(newError);
            // TODO(netwasm-threading)
            s_error = newError;
        }

        public static int Read() => In.Read();

        public static string? ReadLine() => In.ReadLine();

        public static IO.Stream OpenStandardInput() =>
            Runtime.InteropServices.ConsoleServices.ConsoleCompositionRoot.OpenStandardInput();

        // The buffer size is validated and otherwise ignored, as in System.Console.
        public static IO.Stream OpenStandardInput(int bufferSize)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(bufferSize);
            return OpenStandardInput();
        }

        public static IO.Stream OpenStandardOutput() =>
            Runtime.InteropServices.ConsoleServices.ConsoleCompositionRoot.OpenStandardOutput();

        public static IO.Stream OpenStandardOutput(int bufferSize)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(bufferSize);
            return OpenStandardOutput();
        }

        public static IO.Stream OpenStandardError() =>
            Runtime.InteropServices.ConsoleServices.ConsoleCompositionRoot.OpenStandardError();

        public static IO.Stream OpenStandardError(int bufferSize)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(bufferSize);
            return OpenStandardError();
        }

        public static void WriteLine() => Out.WriteLine();

        public static void WriteLine(bool value) => Out.WriteLine(value);

        public static void WriteLine(char value) => Out.WriteLine(value);

        public static void WriteLine(char[]? buffer) => Out.WriteLine(buffer);

        public static void WriteLine(char[] buffer, int index, int count) => Out.WriteLine(buffer, index, count);

        public static void WriteLine(decimal value) => Out.WriteLine(value);

        public static void WriteLine(double value) => Out.WriteLine(value);

        public static void WriteLine(float value) => Out.WriteLine(value);

        public static void WriteLine(int value) => Out.WriteLine(value);

        [CLSCompliant(false)]
        public static void WriteLine(uint value) => Out.WriteLine(value);

        public static void WriteLine(long value) => Out.WriteLine(value);

        [CLSCompliant(false)]
        public static void WriteLine(ulong value) => Out.WriteLine(value);

        public static void WriteLine(object? value) => Out.WriteLine(value);

        public static void WriteLine(string? value) => Out.WriteLine(value);

        public static void WriteLine(ReadOnlySpan<char> value) => Out.WriteLine(value);

        public static void WriteLine(string format, object? arg0) => Out.WriteLine(format, arg0);

        public static void WriteLine(string format, object? arg0, object? arg1) => Out.WriteLine(format, arg0, arg1);

        public static void WriteLine(string format, object? arg0, object? arg1, object? arg2) => Out.WriteLine(format, arg0, arg1, arg2);

        public static void WriteLine(string format, params object?[]? arg)
        {
            if (arg is null)
            {
                Out.WriteLine(format, null, null);
            }
            else
            {
                Out.WriteLine(format, arg);
            }
        }

        public static void WriteLine(string format, params ReadOnlySpan<object?> arg) => Out.WriteLine(format, arg);

        public static void Write(string format, object? arg0) => Out.Write(format, arg0);

        public static void Write(string format, object? arg0, object? arg1) => Out.Write(format, arg0, arg1);

        public static void Write(string format, object? arg0, object? arg1, object? arg2) => Out.Write(format, arg0, arg1, arg2);

        public static void Write(string format, params object?[]? arg)
        {
            if (arg is null)
            {
                Out.Write(format, null, null);
            }
            else
            {
                Out.Write(format, arg);
            }
        }

        public static void Write(string format, params ReadOnlySpan<object?> arg) => Out.Write(format, arg);

        public static void Write(bool value) => Out.Write(value);

        public static void Write(char value) => Out.Write(value);

        public static void Write(char[]? buffer) => Out.Write(buffer);

        public static void Write(char[] buffer, int index, int count) => Out.Write(buffer, index, count);

        public static void Write(double value) => Out.Write(value);

        public static void Write(decimal value) => Out.Write(value);

        public static void Write(float value) => Out.Write(value);

        public static void Write(int value) => Out.Write(value);

        [CLSCompliant(false)]
        public static void Write(uint value) => Out.Write(value);

        public static void Write(long value) => Out.Write(value);

        [CLSCompliant(false)]
        public static void Write(ulong value) => Out.Write(value);

        public static void Write(object? value) => Out.Write(value);

        public static void Write(string? value) => Out.Write(value);

        public static void Write(ReadOnlySpan<char> value) => Out.Write(value);

        private static class StandardInput
        {
            internal static readonly IO.TextReader Reader =
                Runtime.InteropServices.ConsoleServices.ConsoleCompositionRoot
                    .CreateStandardInputReader();
        }

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
