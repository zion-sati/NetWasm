using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class ExceptionTypeNameResolver : IExceptionTypeNameResolver
{
    public string Resolve(ManagedExceptionKind kind) => kind switch
    {
        ManagedExceptionKind.NullReference => "System.NullReferenceException",
        ManagedExceptionKind.IndexOutOfRange => "System.IndexOutOfRangeException",
        ManagedExceptionKind.DivideByZero => "System.DivideByZeroException",
        ManagedExceptionKind.Arithmetic => "System.ArithmeticException",
        ManagedExceptionKind.Overflow => "System.OverflowException",
        ManagedExceptionKind.InvalidCast => "System.InvalidCastException",
        ManagedExceptionKind.OutOfMemory => "System.OutOfMemoryException",
        ManagedExceptionKind.Argument => "System.ArgumentException",
        ManagedExceptionKind.ArgumentNull => "System.ArgumentNullException",
        ManagedExceptionKind.ArgumentOutOfRange => "System.ArgumentOutOfRangeException",
        ManagedExceptionKind.ArrayTypeMismatch => "System.ArrayTypeMismatchException",
        ManagedExceptionKind.JSException => "System.JSException",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
