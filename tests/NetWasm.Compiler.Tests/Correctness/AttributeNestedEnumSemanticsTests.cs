namespace NetWasm.Compiler.Tests.Correctness;

public sealed class AttributeNestedEnumSemanticsTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ClosedNestedEnumPayloadsMatchDesktopAcrossAllArgumentPositions() => runner.Run(new(
        "AttributeNestedEnums", "AttributeNestedEnums", Source, [42])
    {
        ExpectedReturnValue = 42,
        Matrix = new(CorpusMatrixProfile.Family),
    });

    private const string Source = """
        using System;
        using System.Reflection;
        namespace AttributeNestedEnums;

        public class Outer<T>
        {
            public enum Code : ushort { Answer = 42 }
        }
        public sealed class MarkerAttribute(Outer<int>.Code value, Outer<long>.Code[] values, object boxed) : Attribute
        {
            public Outer<int>.Code Value { get; } = value;
            public Outer<long>.Code[] Values { get; } = values;
            public object Boxed { get; } = boxed;
            public object? Named { get; set; }
            public Outer<int>.Code[] Empty { get; set; } = new Outer<int>.Code[1];
            public Outer<int>.Code[]? Null { get; set; } = new Outer<int>.Code[1];
        }
        public sealed class GenericMarkerAttribute<T>(T value) : Attribute
        {
            public T Value { get; } = value;
        }
        [Marker(Outer<int>.Code.Answer, new[] { Outer<long>.Code.Answer }, Outer<long>.Code.Answer,
            Named = Outer<int>.Code.Answer, Empty = new Outer<int>.Code[0], Null = null)]
        [GenericMarker<Outer<int>.Code>(Outer<int>.Code.Answer)]
        public class Target;

        public static class EntryPoint
        {
            public static int Trace() => 0;
            public static int Run(int input)
            {
                var marker = typeof(Target).GetCustomAttribute<MarkerAttribute>()!;
                if (marker.Value != Outer<int>.Code.Answer || marker.Values.Length != 1 ||
                    marker.Values[0] != Outer<long>.Code.Answer) return 1;
                if (marker.Boxed.GetType() != typeof(Outer<long>.Code) ||
                    (Outer<long>.Code)marker.Boxed != Outer<long>.Code.Answer) return 2;
                if (marker.Named?.GetType() != typeof(Outer<int>.Code) ||
                    (Outer<int>.Code)marker.Named != Outer<int>.Code.Answer) return 3;
                if (marker.Empty.Length != 0 || marker.Null is not null) return 4;
                var generic = typeof(Target).GetCustomAttribute<GenericMarkerAttribute<Outer<int>.Code>>()!;
                if (generic.Value != Outer<int>.Code.Answer) return 5;
                return input;
            }
        }
        """;
}
