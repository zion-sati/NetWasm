namespace NetWasm.Compiler.Tests.Correctness;

public sealed class AttributeExceptionSemanticsTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ConstructionAndSetterFailuresPreserveDesktopExceptionSemantics() => runner.Run(new(
        "AttributeExceptions", "AttributeExceptions", Source, [42])
    {
        ExpectedReturnValue = 42,
        Matrix = new(CorpusMatrixProfile.Family),
    });

    private const string Source = """
        using System;
        using System.Reflection;
        namespace AttributeExceptions;

        [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
        public sealed class FailingAttribute : Attribute
        {
            public static int Calls;
            public static readonly Exception Failure = new InvalidOperationException("attribute failure");
            public FailingAttribute(bool fail)
            {
                Calls++;
                if (fail) throw Failure;
            }
            public int Value { get => 0; set { Calls += value; throw Failure; } }
        }
        [Failing(true)] public class ConstructorTarget;
        [Failing(false, Value = 10)] public class SetterTarget;
        [Failing(false, Value = 10), Failing(false)] public class AmbiguousTarget;

        public static class EntryPoint
        {
            public static int Trace() => 0;
            private static int Consume(int prefix, FailingAttribute value) => prefix;
            public static int Run(int input)
            {
                if (!typeof(ConstructorTarget).IsDefined(typeof(FailingAttribute), true) ||
                    !typeof(SetterTarget).IsDefined(typeof(FailingAttribute), true) ||
                    FailingAttribute.Calls != 0) return 1;
                try
                {
                    _ = typeof(ConstructorTarget).GetCustomAttribute<FailingAttribute>();
                    return 2;
                }
                catch (Exception exception)
                {
                    if (!ReferenceEquals(exception, FailingAttribute.Failure) || FailingAttribute.Calls != 1) return 3;
                }
                FailingAttribute.Calls = 0;
                try
                {
                    // A retained caller-stack value must survive query lowering.
                    return Consume(input, typeof(SetterTarget).GetCustomAttribute<FailingAttribute>()!);
                }
                catch (Exception exception)
                {
                    if (!Wrapped(exception) || FailingAttribute.Calls != 11) return 4;
                }
                FailingAttribute.Calls = 0;
                try
                {
                    _ = typeof(AmbiguousTarget).GetCustomAttribute<FailingAttribute>();
                    return 5;
                }
                catch (Exception exception)
                {
                    // The first setter failure precedes the second constructor
                    // and the eventual singular-result ambiguity check.
                    if (!Wrapped(exception) || FailingAttribute.Calls != 11) return 6;
                }
                return input;
            }
            private static bool Wrapped(Exception exception) =>
                exception.GetType().Name == "CustomAttributeFormatException" &&
                exception.HResult == unchecked((int)0x80131605) &&
                exception.Message == "'Value' property specified was not found." &&
                exception.InnerException?.GetType().Name == "TargetInvocationException" &&
                exception.InnerException.HResult == unchecked((int)0x80131604) &&
                ReferenceEquals(exception.InnerException.InnerException, FailingAttribute.Failure);
        }
        """;
}
