namespace NetWasm.Compiler.Tests.Correctness;

public sealed class UnsafeAccessorTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void AccessorKindsAndGeneratedGenericShapesMatchDesktop() =>
        runner.Run(new("UnsafeAccessorContracts", "UnsafeAccessorContracts", Source, [0, 1, 2, 3, 4, 5, 6])
        {
            ExpectedReturnValue = 42,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Simulated),
        });

    // Accessor declaration shapes follow PolyType's SourceFormatter.{Constructors,
    // Properties,Methods}.cs at 5e39fd42766edaafe6d6a39b4cdfcec4b3f539a7.
    // Keep the actual extern signatures; a throwing generator fallback cannot
    // qualify constructor, private/init-only member or generic access.
    internal const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace UnsafeAccessorContracts;

        public sealed class Secret
        {
            private int value;
            private static int shared = 5;
            private Secret(int value) => this.value = value;
            public static Secret Create(int value) => new(value);
            public int Value => value;
            public static int Shared => shared;
            public int Initialized { get; private init; }
            private int Add(int amount) => value + amount;
            private static int Double(int amount) => amount * 2;
        }

        public sealed class GenericSecret<T> where T : struct
        {
            private T value;
            private GenericSecret(T value) => this.value = value;
            public T Value => value;
            private ref T Echo(ref T item, out T copy)
            {
                copy = value;
                return ref item;
            }
        }

        public struct StructSecret
        {
            private int value;
            public int Value => value;
            public StructSecret(int value) => this.value = value;
            private void Add(int amount) => value += amount;
        }

        public static class EntryPoint
        {
            public static int Trace() => 0;
            public static int Run(int input)
            {
                switch (input)
                {
                    case 0: return __CtorAccessor_Secret(42).Value;
                    case 1: return __MethodAccessor_Secret_Add(Secret.Create(40), 2);
                    case 2: return __StaticMethodAccessor_Secret_Double(null!, 21);
                    case 3:
                        var fieldOwner = Secret.Create(1);
                        __FieldAccessor_Secret_value(fieldOwner) = 42;
                        return fieldOwner.Value;
                    case 4:
                        __StaticFieldAccessor_Secret_shared(null!) = 42;
                        return Secret.Shared;
                    case 5:
                        var initialized = Secret.Create(0);
                        __PropertyAccessor_Secret_set_Initialized(initialized, 42);
                        return initialized.Initialized;
                    case 6:
                        var generic = __GenericAccessors<int>.__CtorAccessor(40);
                        int argument = 1;
                        ref int alias = ref __GenericAccessors<int>.__EchoAccessor(generic, ref argument, out int copy);
                        alias = 2;
                        var structure = new StructSecret(copy);
                        __StructMethodAccessor_Add(ref structure, argument);
                        return structure.Value;
                    default: throw new ArgumentOutOfRangeException(nameof(input));
                }
            }

            [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
            private static extern Secret __CtorAccessor_Secret(int value);
            [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Add")]
            private static extern int __MethodAccessor_Secret_Add(Secret obj, int amount);
            [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Double")]
            private static extern int __StaticMethodAccessor_Secret_Double(Secret obj, int amount);
            [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "value")]
            private static extern ref int __FieldAccessor_Secret_value(Secret obj);
            [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "shared")]
            private static extern ref int __StaticFieldAccessor_Secret_shared(Secret obj);
            [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_Initialized")]
            private static extern void __PropertyAccessor_Secret_set_Initialized(Secret obj, int value);
            [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Add")]
            private static extern void __StructMethodAccessor_Add(ref StructSecret obj, int amount);

            private static class __GenericAccessors<T> where T : struct
            {
                [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
                public static extern GenericSecret<T> __CtorAccessor(T value);
                [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Echo")]
                public static extern ref T __EchoAccessor(GenericSecret<T> obj, ref T item, out T copy);
            }
        }
        """;
}
