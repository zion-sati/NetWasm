namespace NetWasm.Compiler.Tests.Correctness;

public sealed class AttributeSelectionSemanticsTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void UsageDefaultsDuplicatesAndInterfaceBoundariesMatchDesktop() => runner.Run(new(
        "AttributeSelection", "AttributeSelection", Source, [42])
    {
        ExpectedReturnValue = 42,
        Matrix = new(CorpusMatrixProfile.Family),
    });

    private const string Source = """
        using System;
        using System.Reflection;
        namespace AttributeSelection;

        [AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
        public class BaseMarkerAttribute(int value) : Attribute
        {
            public int Value { get; } = value;
        }
        public sealed class DerivedMarkerAttribute(int value) : BaseMarkerAttribute(value);

        [DerivedMarker(1), DerivedMarker(2)] public class Base;
        public class Child : Base;
        [DerivedMarker(3)] public class Override : Base;
        [BaseMarker(5)] public class NonInheritedBase;
        public class NonInheritedChild : NonInheritedBase;
        [DerivedMarker(7)] public interface ITagged;
        public interface IExtended : ITagged;
        public class Implementation : IExtended;

        public static class EntryPoint
        {
            public static int Trace() => 0;

            public static int Run(int input)
            {
                var direct = (DerivedMarkerAttribute[])typeof(Base).GetCustomAttributes<DerivedMarkerAttribute>();
                if (direct.Length != 2 || direct[0].Value != 1 || direct[1].Value != 2) return 1;
                var inherited = (BaseMarkerAttribute[])typeof(Child).GetCustomAttributes<BaseMarkerAttribute>(true);
                if (inherited.Length != 2 || inherited[0].Value != 1 || inherited[1].Value != 2) return 2;
                var suppressed = (DerivedMarkerAttribute[])typeof(Override).GetCustomAttributes<DerivedMarkerAttribute>();
                if (suppressed.Length != 1 || suppressed[0].Value != 3) return 3;
                if (!typeof(Child).IsDefined(typeof(DerivedMarkerAttribute), true) ||
                    typeof(Child).IsDefined(typeof(DerivedMarkerAttribute), false)) return 4;
                if (typeof(NonInheritedChild).IsDefined(typeof(BaseMarkerAttribute), true) ||
                    typeof(NonInheritedChild).GetCustomAttribute<BaseMarkerAttribute>() is not null) return 5;
                if (typeof(ITagged).GetCustomAttribute<DerivedMarkerAttribute>()?.Value != 7 ||
                    typeof(IExtended).IsDefined(typeof(DerivedMarkerAttribute), true) ||
                    typeof(Implementation).IsDefined(typeof(DerivedMarkerAttribute), true)) return 6;
                return input;
            }
        }
        """;
}
