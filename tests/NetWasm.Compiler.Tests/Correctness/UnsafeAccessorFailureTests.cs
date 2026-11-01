namespace NetWasm.Compiler.Tests.Correctness;

public sealed class UnsafeAccessorFailureTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void AccessorDispatchAndManagedBindingFailuresMatchDesktop() =>
        runner.Run(new("UnsafeAccessorFailures", "UnsafeAccessorFailures", Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21])
        {
            ExpectedReturnValue = 42,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Simulated),
        });

    internal const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace UnsafeAccessorFailures;

        public class Owner
        {
            private int number = 42;
            public int Number => number;
            private int Method() => number;
            private int Identity<T>(T value) where T : class => value == null ? 0 : 42;
            public virtual int Virtual() => 1;
        }
        public sealed class Derived : Owner
        {
            public override int Virtual() => 42;
        }
        public sealed class GenericOwner<T> where T : class
        {
            private T value;
            private int number = 42;
            public GenericOwner(T value) => this.value = value;
            public T Value => value;
            private int Read() => number;
            private T Echo(T item) => item;
        }
        public struct Structure
        {
            private int value;
            public Structure(int value) => this.value = value;
            public int Value => value;
        }
        public static class EntryPoint
        {
            public static int Trace() => 0;
            public static int Run(int input)
            {
                try
                {
                    switch (input)
                    {
                        case 0: return Virtual(new Derived());
                        case 1: return Missing(new Owner());
                        case 2: return WrongReturn(new Owner()) == 42 ? 1 : 2;
                        case 3: return MissingField(new Owner());
                        case 4: return InstanceNull(null!);
                        case 5: return FieldNull(null!);
                        case 6: return EmptyName(new Owner());
                        case 7: return NullName(new Owner());
                        case 8: return InvalidKind(new Owner());
                        case 9: return BadFieldReturn(new Owner());
                        case 10: return MissingReceiver();
                        case 11: return BadConstructor().Number;
                        case 12: return InheritedOnly(new Derived());
                        case 13: return Strong<string>(new Owner(), "value");
                        case 14: return Weak<string>(new Owner(), "value");
                        case 15: return ClosedRead(new GenericOwner<string>("value"));
                        case 16: return ClosedEcho(new GenericOwner<string>("value"), "value").Length;
                        case 17: return FixedField(new GenericOwner<string>("value"));
                        case 18: return GenericField(new GenericOwner<string>("value")).Length;
                        case 19:
                            var owner = new GenericOwner<string>("before");
                            GenericAccessors<string>.Field(owner) = "after";
                            return owner.Value == "after" ? 42 : 0;
                        case 20:
                            var structure = new Structure(0);
                            StructureField(ref structure) = 42;
                            return structure.Value;
                        case 21: return Method(new Owner());
                    }
                }
                catch (Exception exception)
                {
                    bool expected = input switch
                    {
                        1 or 2 or 6 or 7 or 12 or 16 => exception is MissingMethodException,
                        3 or 18 => exception is MissingFieldException,
                        4 or 5 => exception is NullReferenceException,
                        8 or 9 or 10 or 11 => exception is BadImageFormatException,
                        14 or 15 => exception is InvalidProgramException,
                        _ => false,
                    };
                    return expected ? 42 : -1;
                }
                return -2;
            }
            [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Virtual")]
            private static extern int Virtual(Owner owner);
            [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Absent")]
            private static extern int Missing(Owner owner);
            [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Method")]
            private static extern long WrongReturn(Owner owner);
            [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "absent")]
            private static extern ref int MissingField(Owner owner);
            [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Method")]
            private static extern int InstanceNull(Owner owner);
            [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "number")]
            private static extern ref int FieldNull(Owner owner);
            [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "")]
            private static extern int EmptyName(Owner owner);
            [UnsafeAccessor(UnsafeAccessorKind.Method, Name = null)]
            private static extern int NullName(Owner owner);
            [UnsafeAccessor((UnsafeAccessorKind)99)]
            private static extern int InvalidKind(Owner owner);
            [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "number")]
            private static extern int BadFieldReturn(Owner owner);
            [UnsafeAccessor(UnsafeAccessorKind.Method)]
            private static extern int MissingReceiver();
            [UnsafeAccessor(UnsafeAccessorKind.Constructor, Name = ".ctor")]
            private static extern Owner BadConstructor();
            [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Method")]
            private static extern int InheritedOnly(Derived owner);
            [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Identity")]
            private static extern int Strong<T>(Owner owner, T value) where T : class;
            [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Identity")]
            private static extern int Weak<T>(Owner owner, T value);
            [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Read")]
            private static extern int ClosedRead(GenericOwner<string> owner);
            [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Echo")]
            private static extern string ClosedEcho(GenericOwner<string> owner, string value);
            [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "number")]
            private static extern ref int FixedField(GenericOwner<string> owner);
            [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "value")]
            private static extern ref string GenericField(GenericOwner<string> owner);
            [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "value")]
            private static extern ref int StructureField(ref Structure owner);
            [UnsafeAccessor(UnsafeAccessorKind.Method)]
            private static extern int Method(Owner owner);
            private static class GenericAccessors<T> where T : class
            {
                [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "value")]
                public static extern ref T Field(GenericOwner<T> owner);
            }
        }
        """;
}
