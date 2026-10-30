// Adapted from dotnet/runtime System.Private.CoreLib exception implementations.
// AggregateException support and dictionary-enumerator wiring are based on
// dotnet/runtime commit 474ed963699fc29809015392738a15231aaae2de.
// The upstream implementation is licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System
{
    public class Exception
    {
        private readonly string? _message;
        private readonly Exception? _innerException;
        private string? _source;
        private string? _helpLink;
        private string? _stackTrace = null;
        private int _hResult;
        private Collections.IDictionary? _data;

        public Exception() => HResult = HResults.COR_E_EXCEPTION;
        public Exception(string? message)
        {
            _message = message;
            HResult = HResults.COR_E_EXCEPTION;
        }
        public Exception(string? message, Exception? innerException)
        {
            _message = message;
            _innerException = innerException;
            HResult = HResults.COR_E_EXCEPTION;
        }

        public virtual string Message
        {
            get => _message ?? "An exception was thrown.";
        }
        public Exception? InnerException
        {
            get => _innerException;
        }
        public new Type GetType() => base.GetType();
        public virtual string? StackTrace => _stackTrace;
        public virtual string? Source { get => _source; set => _source = value; }
        public virtual string? HelpLink { get => _helpLink; set => _helpLink = value; }
        public int HResult { get => _hResult; set => _hResult = value; }
        public virtual Collections.IDictionary Data
        {
            get => _data ??= new Collections.ListDictionaryInternal();
        }

        public virtual Exception GetBaseException()
        {
            var current = this;
            while (current.InnerException is not null)
            {
                current = current.InnerException;
            }
            return current;
        }

        public override string ToString() => _innerException is null
            ? Message
            : Message + " ---> " + _innerException.ToString();
    }

    public class SystemException : Exception
    {
        public SystemException() => HResult = HResults.COR_E_SYSTEM;
        public SystemException(string? message) : base(message) => HResult = HResults.COR_E_SYSTEM;
        public SystemException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_SYSTEM;
    }

    public class PlatformNotSupportedException : NotSupportedException
    {
        public PlatformNotSupportedException() => HResult = HResults.COR_E_PLATFORMNOTSUPPORTED;
        public PlatformNotSupportedException(string? message) : base(message) => HResult = HResults.COR_E_PLATFORMNOTSUPPORTED;
        public PlatformNotSupportedException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_PLATFORMNOTSUPPORTED;
    }

    public class NotSupportedException : SystemException
    {
        public NotSupportedException() => HResult = HResults.COR_E_NOTSUPPORTED;
        public NotSupportedException(string? message) : base(message) => HResult = HResults.COR_E_NOTSUPPORTED;
        public NotSupportedException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_NOTSUPPORTED;
    }

    public class InvalidOperationException : SystemException
    {
        public InvalidOperationException() => HResult = HResults.COR_E_INVALIDOPERATION;
        public InvalidOperationException(string? message) : base(message) => HResult = HResults.COR_E_INVALIDOPERATION;
        public InvalidOperationException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_INVALIDOPERATION;
    }

    public class ObjectDisposedException : InvalidOperationException
    {
        private readonly string? _objectName;

        private ObjectDisposedException() : this(null, (string?)null) { }

        public ObjectDisposedException(string? objectName) : this(objectName, (string?)null) { }

        public ObjectDisposedException(string? objectName, string? message) : base(message)
        {
            _objectName = objectName;
            HResult = HResults.COR_E_OBJECTDISPOSED;
        }
        public ObjectDisposedException(string? message, Exception? innerException) : base(message, innerException) =>
            HResult = HResults.COR_E_OBJECTDISPOSED;
        public string ObjectName => _objectName ?? string.Empty;

        public override string Message
        {
            get => ObjectName is null || ObjectName.Length == 0
                ? base.Message
                : base.Message + " (Object name: '" + ObjectName + "')";
        }

        // Runtime type-name metadata is intentionally unavailable in the
        // NetWasm profile, so these guards preserve the exception contract
        // without synthesizing an object name.
        public static void ThrowIf([DoesNotReturnIf(true)] bool condition, object instance)
        {
            if (condition) throw new ObjectDisposedException(null);
        }

        public static void ThrowIf([DoesNotReturnIf(true)] bool condition, Type type)
        {
            if (condition) throw new ObjectDisposedException(null);
        }
    }

    public class OperationCanceledException : SystemException
    {
        private readonly Threading.CancellationToken _cancellationToken;

        public OperationCanceledException() => HResult = HResults.COR_E_OPERATIONCANCELED;
        public OperationCanceledException(string? message) : base(message) => HResult = HResults.COR_E_OPERATIONCANCELED;
        public OperationCanceledException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_OPERATIONCANCELED;
        public OperationCanceledException(Threading.CancellationToken token)
            : this(null, null, token)
        {
        }
        public OperationCanceledException(
            string? message,
            Threading.CancellationToken token)
            : this(message, null, token)
        {
        }
        public OperationCanceledException(
            string? message,
            Exception? innerException,
            Threading.CancellationToken token)
            : base(message, innerException)
        {
            HResult = HResults.COR_E_OPERATIONCANCELED;
            _cancellationToken = token;
        }

        public Threading.CancellationToken CancellationToken => _cancellationToken;
    }

    public class NullReferenceException : SystemException
    {
        public NullReferenceException() => HResult = HResults.E_POINTER;
        public NullReferenceException(string? message) : base(message) => HResult = HResults.E_POINTER;
        public NullReferenceException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.E_POINTER;
    }

    public class IndexOutOfRangeException : SystemException
    {
        public IndexOutOfRangeException() => HResult = HResults.COR_E_INDEXOUTOFRANGE;
        public IndexOutOfRangeException(string? message) : base(message) => HResult = HResults.COR_E_INDEXOUTOFRANGE;
        public IndexOutOfRangeException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_INDEXOUTOFRANGE;
    }

    public class ArithmeticException : SystemException
    {
        public ArithmeticException() => HResult = HResults.COR_E_ARITHMETIC;
        public ArithmeticException(string? message) : base(message) => HResult = HResults.COR_E_ARITHMETIC;
        public ArithmeticException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_ARITHMETIC;
    }

    public class DivideByZeroException : ArithmeticException
    {
        public DivideByZeroException() => HResult = HResults.COR_E_DIVIDEBYZERO;
        public DivideByZeroException(string? message) : base(message) => HResult = HResults.COR_E_DIVIDEBYZERO;
        public DivideByZeroException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_DIVIDEBYZERO;
    }

    public class OverflowException : ArithmeticException
    {
        public OverflowException() => HResult = HResults.COR_E_OVERFLOW;
        public OverflowException(string? message) : base(message) => HResult = HResults.COR_E_OVERFLOW;
        public OverflowException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_OVERFLOW;
    }

    public class FormatException : SystemException
    {
        public FormatException() => HResult = HResults.COR_E_FORMAT;
        public FormatException(string? message) : base(message) => HResult = HResults.COR_E_FORMAT;
        public FormatException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_FORMAT;
    }

    public class InvalidCastException : SystemException
    {
        public InvalidCastException() => HResult = HResults.COR_E_INVALIDCAST;
        public InvalidCastException(string? message) : base(message) => HResult = HResults.COR_E_INVALIDCAST;
        public InvalidCastException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_INVALIDCAST;
        public InvalidCastException(string? message, int hResult) : base(message) => HResult = hResult;
    }

    public class OutOfMemoryException : SystemException
    {
        public OutOfMemoryException() => HResult = HResults.COR_E_OUTOFMEMORY;
        public OutOfMemoryException(string? message) : base(message) => HResult = HResults.COR_E_OUTOFMEMORY;
        public OutOfMemoryException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_OUTOFMEMORY;
    }

    public class ArgumentException : SystemException
    {
        public ArgumentException() => HResult = HResults.COR_E_ARGUMENT;
        public ArgumentException(string? message) : base(message) => HResult = HResults.COR_E_ARGUMENT;
        public ArgumentException(string? message, string? paramName) : base(message)
        {
            ParamName = paramName;
            HResult = HResults.COR_E_ARGUMENT;
        }
        public ArgumentException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_ARGUMENT;
        public ArgumentException(string? message, string? paramName, Exception? innerException) : base(message, innerException)
        {
            ParamName = paramName;
            HResult = HResults.COR_E_ARGUMENT;
        }
        public virtual string? ParamName { get; }

        public override string Message
        {
            get => ParamName is null || ParamName.Length == 0
                ? base.Message
                : base.Message + " (Parameter '" + ParamName + "')";
        }

        // Preserve upstream caller-expression metadata so ported callers that
        // omit paramName still report the offending argument, including RNG.
        public static void ThrowIfNullOrEmpty([NotNull] string? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument is null) throw new ArgumentNullException(paramName);
            if (argument.Length == 0)
                throw new ArgumentException("Value cannot be empty.", paramName);
        }

        public static void ThrowIfNullOrWhiteSpace([NotNull] string? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument is null) throw new ArgumentNullException(paramName);
            if (string.IsNullOrWhiteSpace(argument))
                throw new ArgumentException("The value cannot be whitespace.", paramName);
        }
    }

    public class ArgumentNullException : ArgumentException
    {
        public ArgumentNullException() => HResult = HResults.E_POINTER;
        public ArgumentNullException(string? paramName) : base(null, paramName) => HResult = HResults.E_POINTER;
        public ArgumentNullException(string? paramName, string? message) : base(message, paramName) => HResult = HResults.E_POINTER;
        public ArgumentNullException(string? paramName, Exception? innerException) : base(null, paramName, innerException) => HResult = HResults.E_POINTER;

        public static void ThrowIfNull([NotNull] object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument is null) throw new ArgumentNullException(paramName);
        }

        public static unsafe void ThrowIfNull([NotNull] void* argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument is null) throw new ArgumentNullException(paramName);
        }
    }

    public class ArgumentOutOfRangeException : ArgumentException
    {
        public ArgumentOutOfRangeException() => HResult = HResults.COR_E_ARGUMENTOUTOFRANGE;
        public ArgumentOutOfRangeException(string? paramName) : base(null, paramName) => HResult = HResults.COR_E_ARGUMENTOUTOFRANGE;
        public ArgumentOutOfRangeException(string? paramName, string? message) : base(message, paramName) => HResult = HResults.COR_E_ARGUMENTOUTOFRANGE;
        public ArgumentOutOfRangeException(string? paramName, object? actualValue, string? message) : base(message, paramName)
        {
            ActualValue = actualValue;
            HResult = HResults.COR_E_ARGUMENTOUTOFRANGE;
        }
        public virtual object? ActualValue { get; }
        public override string Message
        {
            get => base.Message;
        }

        public ArgumentOutOfRangeException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_ARGUMENTOUTOFRANGE;

        public static void ThrowIfEqual<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        {
            if (EqualityComparer<T>.Default.Equals(value, other)) throw new ArgumentOutOfRangeException(paramName, value, "The value must not be equal to the other value.");
        }

        public static void ThrowIfNotEqual<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        {
            if (!EqualityComparer<T>.Default.Equals(value, other)) throw new ArgumentOutOfRangeException(paramName, value, "The value must be equal to the other value.");
        }

        public static void ThrowIfGreaterThan<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : IComparable<T>
        {
            if (value.CompareTo(other) > 0) throw new ArgumentOutOfRangeException(paramName, value, "The value must be less than or equal to the other value.");
        }

        public static void ThrowIfGreaterThanOrEqual<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : IComparable<T>
        {
            if (value.CompareTo(other) >= 0) throw new ArgumentOutOfRangeException(paramName, value, "The value must be less than the other value.");
        }

        public static void ThrowIfLessThan<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : IComparable<T>
        {
            if (value.CompareTo(other) < 0) throw new ArgumentOutOfRangeException(paramName, value, "The value must be greater than or equal to the other value.");
        }

        public static void ThrowIfLessThanOrEqual<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : IComparable<T>
        {
            if (value.CompareTo(other) <= 0) throw new ArgumentOutOfRangeException(paramName, value, "The value must be greater than the other value.");
        }

        public static void ThrowIfNegativeOrZero<T>(T value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : Numerics.INumberBase<T>
        {
            if (T.IsNegative(value) || T.IsZero(value))
                throw new ArgumentOutOfRangeException(paramName, value, "The value must be positive.");
        }

        public static void ThrowIfNegative<T>(T value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : Numerics.INumberBase<T>
        {
            if (T.IsNegative(value))
                throw new ArgumentOutOfRangeException(paramName, value, "The value must be non-negative.");
        }

        public static void ThrowIfZero<T>(T value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : Numerics.INumberBase<T>
        {
            if (T.IsZero(value))
                throw new ArgumentOutOfRangeException(paramName, value, "The value must be non-zero.");
        }
    }

    public class ArrayTypeMismatchException : SystemException
    {
        public ArrayTypeMismatchException() => HResult = HResults.COR_E_ARRAYTYPEMISMATCH;
        public ArrayTypeMismatchException(string? message) : base(message) => HResult = HResults.COR_E_ARRAYTYPEMISMATCH;
        public ArrayTypeMismatchException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_ARRAYTYPEMISMATCH;
    }
}

namespace System
{
    public class AccessViolationException : SystemException { public AccessViolationException() => HResult = HResults.E_POINTER; public AccessViolationException(string? message) : base(message) => HResult = HResults.E_POINTER; public AccessViolationException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.E_POINTER; }
    public class AppDomainUnloadedException : SystemException { public AppDomainUnloadedException() => HResult = HResults.COR_E_APPDOMAINUNLOADED; public AppDomainUnloadedException(string? message) : base(message) => HResult = HResults.COR_E_APPDOMAINUNLOADED; public AppDomainUnloadedException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_APPDOMAINUNLOADED; }
    public class ApplicationException : Exception { public ApplicationException() => HResult = HResults.COR_E_APPLICATION; public ApplicationException(string? message) : base(message) => HResult = HResults.COR_E_APPLICATION; public ApplicationException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_APPLICATION; }
    public class CannotUnloadAppDomainException : SystemException { public CannotUnloadAppDomainException() => HResult = HResults.COR_E_CANNOTUNLOADAPPDOMAIN; public CannotUnloadAppDomainException(string? message) : base(message) => HResult = HResults.COR_E_CANNOTUNLOADAPPDOMAIN; public CannotUnloadAppDomainException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_CANNOTUNLOADAPPDOMAIN; }
    public class ContextMarshalException : SystemException { public ContextMarshalException() => HResult = HResults.COR_E_CONTEXTMARSHAL; public ContextMarshalException(string? message) : base(message) => HResult = HResults.COR_E_CONTEXTMARSHAL; public ContextMarshalException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_CONTEXTMARSHAL; }
    public class DataMisalignedException : SystemException { public DataMisalignedException() => HResult = HResults.COR_E_DATAMISALIGNED; public DataMisalignedException(string? message) : base(message) => HResult = HResults.COR_E_DATAMISALIGNED; public DataMisalignedException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_DATAMISALIGNED; }
    public class ExecutionEngineException : SystemException { public ExecutionEngineException() => HResult = HResults.COR_E_EXECUTIONENGINE; public ExecutionEngineException(string? message) : base(message) => HResult = HResults.COR_E_EXECUTIONENGINE; public ExecutionEngineException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_EXECUTIONENGINE; }
    public class InsufficientExecutionStackException : SystemException { public InsufficientExecutionStackException() => HResult = HResults.COR_E_INSUFFICIENTEXECUTIONSTACK; public InsufficientExecutionStackException(string? message) : base(message) => HResult = HResults.COR_E_INSUFFICIENTEXECUTIONSTACK; public InsufficientExecutionStackException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_INSUFFICIENTEXECUTIONSTACK; }
    public class InsufficientMemoryException : OutOfMemoryException { public InsufficientMemoryException() => HResult = HResults.COR_E_INSUFFICIENTMEMORY; public InsufficientMemoryException(string? message) : base(message) => HResult = HResults.COR_E_INSUFFICIENTMEMORY; public InsufficientMemoryException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_INSUFFICIENTMEMORY; }
    public class InvalidProgramException : SystemException { public InvalidProgramException() => HResult = HResults.COR_E_INVALIDPROGRAM; public InvalidProgramException(string? message) : base(message) => HResult = HResults.COR_E_INVALIDPROGRAM; public InvalidProgramException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_INVALIDPROGRAM; }
    public class InvalidTimeZoneException : Exception { public InvalidTimeZoneException() { } public InvalidTimeZoneException(string? message) : base(message) { } public InvalidTimeZoneException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class MemberAccessException : SystemException { public MemberAccessException() => HResult = HResults.COR_E_MEMBERACCESS; public MemberAccessException(string? message) : base(message) => HResult = HResults.COR_E_MEMBERACCESS; public MemberAccessException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_MEMBERACCESS; }
    public class MethodAccessException : MemberAccessException { public MethodAccessException() => HResult = HResults.COR_E_METHODACCESS; public MethodAccessException(string? message) : base(message) => HResult = HResults.COR_E_METHODACCESS; public MethodAccessException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_METHODACCESS; }
    public class MulticastNotSupportedException : SystemException { public MulticastNotSupportedException() => HResult = HResults.COR_E_MULTICASTNOTSUPPORTED; public MulticastNotSupportedException(string? message) : base(message) => HResult = HResults.COR_E_MULTICASTNOTSUPPORTED; public MulticastNotSupportedException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_MULTICASTNOTSUPPORTED; }
    public class NotImplementedException : SystemException { public NotImplementedException() => HResult = HResults.E_NOTIMPL; public NotImplementedException(string? message) : base(message) => HResult = HResults.E_NOTIMPL; public NotImplementedException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.E_NOTIMPL; }
    public class RankException : SystemException { public RankException() => HResult = HResults.COR_E_RANK; public RankException(string? message) : base(message) => HResult = HResults.COR_E_RANK; public RankException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_RANK; }
    public class StackOverflowException : SystemException { public StackOverflowException() => HResult = HResults.COR_E_STACKOVERFLOW; public StackOverflowException(string? message) : base(message) => HResult = HResults.COR_E_STACKOVERFLOW; public StackOverflowException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_STACKOVERFLOW; }
    public class TimeoutException : SystemException { public TimeoutException() => HResult = HResults.COR_E_TIMEOUT; public TimeoutException(string? message) : base(message) => HResult = HResults.COR_E_TIMEOUT; public TimeoutException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_TIMEOUT; }
    public class TimeZoneNotFoundException : Exception { public TimeZoneNotFoundException() { } public TimeZoneNotFoundException(string? message) : base(message) { } public TimeZoneNotFoundException(string? message, Exception? innerException) : base(message, innerException) { } }
    public class TypeUnloadedException : SystemException { public TypeUnloadedException() => HResult = HResults.COR_E_TYPEUNLOADED; public TypeUnloadedException(string? message) : base(message) => HResult = HResults.COR_E_TYPEUNLOADED; public TypeUnloadedException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_TYPEUNLOADED; }
    public class UnauthorizedAccessException : SystemException { public UnauthorizedAccessException() => HResult = HResults.COR_E_UNAUTHORIZEDACCESS; public UnauthorizedAccessException(string? message) : base(message) => HResult = HResults.COR_E_UNAUTHORIZEDACCESS; public UnauthorizedAccessException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_UNAUTHORIZEDACCESS; }

    public class BadImageFormatException : SystemException
    {
        private readonly string? _fileName;
        private readonly string? _fusionLog = null;
        public BadImageFormatException() => HResult = HResults.COR_E_BADIMAGEFORMAT;
        public BadImageFormatException(string? message) : base(message) => HResult = HResults.COR_E_BADIMAGEFORMAT;
        public BadImageFormatException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_BADIMAGEFORMAT;
        public BadImageFormatException(string? message, string? fileName) : base(message)
        {
            _fileName = fileName;
            HResult = HResults.COR_E_BADIMAGEFORMAT;
        }
        public BadImageFormatException(string? message, string? fileName, Exception? innerException) : base(message, innerException)
        {
            _fileName = fileName;
            HResult = HResults.COR_E_BADIMAGEFORMAT;
        }
        public string? FileName
        {
            get => _fileName;
        }
        public string? FusionLog
        {
            get => _fusionLog;
        }
        public override string Message
        {
            get => base.Message;
        }
        public override string ToString()
        {
            return base.ToString();
        }
    }

    public class DuplicateWaitObjectException : ArgumentException
    {
        public DuplicateWaitObjectException() => HResult = HResults.COR_E_DUPLICATEWAITOBJECT;
        public DuplicateWaitObjectException(string? parameterName) : base(null, parameterName)
        {
            HResult = HResults.COR_E_DUPLICATEWAITOBJECT;
        }
        public DuplicateWaitObjectException(string? parameterName, string? message) : base(message, parameterName)
        {
            HResult = HResults.COR_E_DUPLICATEWAITOBJECT;
        }
        public DuplicateWaitObjectException(string? message, Exception? innerException) : base(message, innerException)
        {
            HResult = HResults.COR_E_DUPLICATEWAITOBJECT;
        }
    }

    public class EntryPointNotFoundException : TypeLoadException { public EntryPointNotFoundException() => HResult = HResults.COR_E_ENTRYPOINTNOTFOUND; public EntryPointNotFoundException(string? message) : base(message) => HResult = HResults.COR_E_ENTRYPOINTNOTFOUND; public EntryPointNotFoundException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_ENTRYPOINTNOTFOUND; }
    public class FieldAccessException : MemberAccessException { public FieldAccessException() => HResult = HResults.COR_E_FIELDACCESS; public FieldAccessException(string? message) : base(message) => HResult = HResults.COR_E_FIELDACCESS; public FieldAccessException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_FIELDACCESS; }
    public class DllNotFoundException : TypeLoadException { public DllNotFoundException() => HResult = HResults.COR_E_DLLNOTFOUND; public DllNotFoundException(string? message) : base(message) => HResult = HResults.COR_E_DLLNOTFOUND; public DllNotFoundException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_DLLNOTFOUND; }

    public class MissingMemberException : MemberAccessException
    {
        protected string? ClassName;
        protected string? MemberName;

        public MissingMemberException() => HResult = HResults.COR_E_MISSINGMEMBER;
        public MissingMemberException(string? message) : base(message) => HResult = HResults.COR_E_MISSINGMEMBER;
        public MissingMemberException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_MISSINGMEMBER;
        public MissingMemberException(string? className, string? memberName)
        {
            ClassName = className;
            MemberName = memberName;
            HResult = HResults.COR_E_MISSINGMEMBER;
        }
        public override string Message
        {
            get => ClassName is null
                ? base.Message
                : "Member '" + ClassName + "." + MemberName + "' not found.";
        }
    }
    public class MissingFieldException : MissingMemberException
    {
        public MissingFieldException() => HResult = HResults.COR_E_MISSINGFIELD;
        public MissingFieldException(string? message) : base(message) => HResult = HResults.COR_E_MISSINGFIELD;
        public MissingFieldException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_MISSINGFIELD;
        public MissingFieldException(string? className, string? fieldName) : base(className, fieldName) => HResult = HResults.COR_E_MISSINGFIELD;
        public override string Message => ClassName is null
            ? base.Message
            : "Field '" + ClassName + "." + MemberName + "' not found.";
    }
    public class MissingMethodException : MissingMemberException
    {
        public MissingMethodException() => HResult = HResults.COR_E_MISSINGMETHOD;
        public MissingMethodException(string? message) : base(message) => HResult = HResults.COR_E_MISSINGMETHOD;
        public MissingMethodException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_MISSINGMETHOD;
        public MissingMethodException(string? className, string? methodName) : base(className, methodName) => HResult = HResults.COR_E_MISSINGMETHOD;
        public override string Message => ClassName is null
            ? base.Message
            : "Method '" + ClassName + "." + MemberName + "' not found.";
    }

    public class NotFiniteNumberException : ArithmeticException
    {
        private readonly double _offendingNumber;
        public NotFiniteNumberException() => HResult = HResults.COR_E_NOTFINITENUMBER;
        public NotFiniteNumberException(double offendingNumber)
        {
            _offendingNumber = offendingNumber;
            HResult = HResults.COR_E_NOTFINITENUMBER;
        }
        public NotFiniteNumberException(string? message) : base(message) => HResult = HResults.COR_E_NOTFINITENUMBER;
        public NotFiniteNumberException(string? message, double offendingNumber) : base(message)
        {
            _offendingNumber = offendingNumber;
            HResult = HResults.COR_E_NOTFINITENUMBER;
        }
        public NotFiniteNumberException(string? message, double offendingNumber, Exception? innerException) : base(message, innerException)
        {
            _offendingNumber = offendingNumber;
            HResult = HResults.COR_E_NOTFINITENUMBER;
        }
        public NotFiniteNumberException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_NOTFINITENUMBER;
        public double OffendingNumber
        {
            get => _offendingNumber;
        }
    }

    public class TypeLoadException : SystemException
    {
        private readonly string? _typeName = null;
        public TypeLoadException() => HResult = HResults.COR_E_TYPELOAD;
        public TypeLoadException(string? message) : base(message) => HResult = HResults.COR_E_TYPELOAD;
        public TypeLoadException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_TYPELOAD;
        public string TypeName => _typeName ?? string.Empty;
        public override string Message
        {
            get => base.Message;
        }
    }
    public class TypeAccessException : TypeLoadException { public TypeAccessException() => HResult = HResults.COR_E_TYPEACCESS; public TypeAccessException(string? message) : base(message) => HResult = HResults.COR_E_TYPEACCESS; public TypeAccessException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_TYPEACCESS; }
    public sealed class TypeInitializationException : SystemException
    {
        private readonly string? _typeName;
        public TypeInitializationException(string? typeName, Exception? innerException) : base("The type initializer for '" + typeName + "' threw an exception.", innerException)
        {
            _typeName = typeName;
            HResult = HResults.COR_E_TYPEINITIALIZATION;
        }
        public string TypeName => _typeName ?? string.Empty;
    }
}

namespace System.Diagnostics
{
    public class UnreachableException : System.Exception
    {
        public UnreachableException() { }
        public UnreachableException(string? message) : base(message) { }
        public UnreachableException(string? message, System.Exception? innerException) : base(message, innerException) { }
    }
}

namespace System.Runtime
{
    public class AmbiguousImplementationException : System.Exception
    {
        public AmbiguousImplementationException() => HResult = System.HResults.COR_E_AMBIGUOUSIMPLEMENTATION;
        public AmbiguousImplementationException(string? message) : base(message) => HResult = System.HResults.COR_E_AMBIGUOUSIMPLEMENTATION;
        public AmbiguousImplementationException(string? message, System.Exception? innerException) : base(message, innerException) => HResult = System.HResults.COR_E_AMBIGUOUSIMPLEMENTATION;
    }
}

namespace System.Runtime.CompilerServices
{
    public class RuntimeWrappedException : System.Exception
    {
        private readonly object? _wrappedException;
        public RuntimeWrappedException(object? thrownObject)
        {
            _wrappedException = thrownObject;
            HResult = System.HResults.COR_E_RUNTIMEWRAPPED;
        }
        public object? WrappedException
        {
            get => _wrappedException;
        }
    }

    public class SwitchExpressionException : System.InvalidOperationException
    {
        private readonly object? _unmatchedValue;
        public SwitchExpressionException() { }
        public SwitchExpressionException(System.Exception? innerException) : base(null, innerException) { }
        public SwitchExpressionException(object? unmatchedValue) => _unmatchedValue = unmatchedValue;
        public SwitchExpressionException(string? message) : base(message) { }
        public SwitchExpressionException(string? message, System.Exception? innerException) : base(message, innerException) { }
        public override string Message
        {
            get => base.Message;
        }
        public object? UnmatchedValue
        {
            get => _unmatchedValue;
        }
    }
}

namespace System.Runtime.InteropServices
{
    public class InvalidComObjectException : SystemException { public InvalidComObjectException() => HResult = HResults.COR_E_INVALIDCOMOBJECT; public InvalidComObjectException(string? message) : base(message) => HResult = HResults.COR_E_INVALIDCOMOBJECT; public InvalidComObjectException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_INVALIDCOMOBJECT; }
    public class InvalidOleVariantTypeException : SystemException { public InvalidOleVariantTypeException() => HResult = HResults.COR_E_INVALIDOLEVARIANTTYPE; public InvalidOleVariantTypeException(string? message) : base(message) => HResult = HResults.COR_E_INVALIDOLEVARIANTTYPE; public InvalidOleVariantTypeException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_INVALIDOLEVARIANTTYPE; }
    public class MarshalDirectiveException : SystemException { public MarshalDirectiveException() => HResult = HResults.COR_E_MARSHALDIRECTIVE; public MarshalDirectiveException(string? message) : base(message) => HResult = HResults.COR_E_MARSHALDIRECTIVE; public MarshalDirectiveException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_MARSHALDIRECTIVE; }
    public class SafeArrayRankMismatchException : SystemException { public SafeArrayRankMismatchException() => HResult = HResults.COR_E_SAFEARRAYRANKMISMATCH; public SafeArrayRankMismatchException(string? message) : base(message) => HResult = HResults.COR_E_SAFEARRAYRANKMISMATCH; public SafeArrayRankMismatchException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_SAFEARRAYRANKMISMATCH; }
    public class SafeArrayTypeMismatchException : SystemException { public SafeArrayTypeMismatchException() => HResult = HResults.COR_E_SAFEARRAYTYPEMISMATCH; public SafeArrayTypeMismatchException(string? message) : base(message) => HResult = HResults.COR_E_SAFEARRAYTYPEMISMATCH; public SafeArrayTypeMismatchException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_SAFEARRAYTYPEMISMATCH; }
}

namespace System.Security.Cryptography
{
    public class CryptographicException : SystemException
    {
        public CryptographicException() { }
        public CryptographicException(int hResult) => HResult = hResult;
        public CryptographicException(string? message) : base(message) { }
        public CryptographicException(string? message, Exception? innerException) : base(message, innerException) { }
        public CryptographicException(string format, string? insert) : base(string.Format(format, insert)) { }
    }
}

namespace System.Security
{
    public class SecurityException : SystemException
    {
        private object? _demanded;
        private object? _denySetInstance;
        private string? _grantedSet;
        private string? _permissionState;
        private Type? _permissionType;
        private object? _permitOnlySetInstance;
        private string? _refusedSet;
        private string? _url;
        public SecurityException() => HResult = HResults.COR_E_SECURITY;
        public SecurityException(string? message) : base(message) => HResult = HResults.COR_E_SECURITY;
        public SecurityException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_SECURITY;
        public SecurityException(string? message, Type? type) : base(message)
        {
            _permissionType = type;
            HResult = HResults.COR_E_SECURITY;
        }
        public SecurityException(string? message, Type? type, string? state) : base(message)
        {
            _permissionType = type;
            _permissionState = state;
            HResult = HResults.COR_E_SECURITY;
        }
        public object? Demanded { get => _demanded; set => _demanded = value; }
        public object? DenySetInstance { get => _denySetInstance; set => _denySetInstance = value; }
        public string? GrantedSet { get => _grantedSet; set => _grantedSet = value; }
        public string? PermissionState { get => _permissionState; set => _permissionState = value; }
        public Type? PermissionType { get => _permissionType; set => _permissionType = value; }
        public object? PermitOnlySetInstance { get => _permitOnlySetInstance; set => _permitOnlySetInstance = value; }
        public string? RefusedSet { get => _refusedSet; set => _refusedSet = value; }
        public string? Url { get => _url; set => _url = value; }

        public override string ToString()
        {
            return base.ToString();
        }
    }

    public class VerificationException : SystemException { public VerificationException() => HResult = HResults.COR_E_VERIFICATION; public VerificationException(string? message) : base(message) => HResult = HResults.COR_E_VERIFICATION; public VerificationException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_VERIFICATION; }
}

namespace System.Runtime.ExceptionServices
{
    public sealed class ExceptionDispatchInfo
    {
        private readonly System.Exception _sourceException;
        private ExceptionDispatchInfo(System.Exception sourceException) => _sourceException = sourceException;
        public System.Exception SourceException
        {
            get => _sourceException;
        }
        public static ExceptionDispatchInfo Capture(System.Exception sourceException) =>
            sourceException is null ? throw new System.ArgumentNullException(nameof(sourceException)) : new(sourceException);
        public void Throw() => InternalThrow(_sourceException);
        public static void Throw(System.Exception sourceException) =>
            InternalThrow(sourceException ?? throw new System.ArgumentNullException(
                nameof(sourceException)));

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
        private static extern void InternalThrow(System.Exception sourceException);
    }
}

namespace System.Collections.Generic
{
    public class KeyNotFoundException : SystemException
    {
        public KeyNotFoundException() => HResult = HResults.COR_E_KEYNOTFOUND;
        public KeyNotFoundException(string? message) : base(message) => HResult = HResults.COR_E_KEYNOTFOUND;
        public KeyNotFoundException(string? message, Exception? innerException) : base(message, innerException) => HResult = HResults.COR_E_KEYNOTFOUND;
    }
}
